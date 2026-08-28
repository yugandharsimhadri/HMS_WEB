using System.Diagnostics;
using Microsoft.Playwright;

namespace SivayaanHMS.Automation;

/// <summary>
/// Launches Chromium, installing the build Playwright expects only if it turns out to be
/// missing. Ported unchanged in spirit from TransTrack.Automation's helper of the same name.
///
/// The install is deliberately not run up front on every session: <c>playwright install</c>
/// removes browser builds no longer referenced by the installed version, so calling it routinely
/// would keep evicting the builds other projects on this machine (TransTrack, ABPS_WEB) depend
/// on. Installing only on an actual launch failure keeps the shared browser cache stable.
/// </summary>
public static class BrowserProvisioning
{
    private static readonly SemaphoreSlim InstallLock = new(1, 1);
    private static bool _installAttempted;

    public static async Task<IBrowser> LaunchChromiumAsync(
        IPlaywright playwright,
        BrowserTypeLaunchOptions launchOptions,
        Action<string>? log = null)
    {
        try
        {
            return await playwright.Chromium.LaunchAsync(launchOptions);
        }
        catch (PlaywrightException ex) when (IsMissingBrowser(ex))
        {
            log?.Invoke("The Chromium build Playwright expects is not installed. Installing it now (one time).");

            await InstallChromiumAsync(log);

            return await playwright.Chromium.LaunchAsync(launchOptions);
        }
    }

    private static bool IsMissingBrowser(PlaywrightException ex)
        => ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("playwright install", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs the playwright.ps1 script the Microsoft.Playwright package drops next to the built
    /// assembly, as a child process with a timeout — out of process rather than calling
    /// Microsoft.Playwright.Program.Main so a stalled download cannot wedge the test host with no
    /// way to observe or cancel it.
    /// </summary>
    private static async Task InstallChromiumAsync(Action<string>? log)
    {
        await InstallLock.WaitAsync();
        try
        {
            if (_installAttempted)
                throw new InvalidOperationException(
                    "Chromium is still unavailable after an install attempt in this run. " +
                    "Install it manually with: pwsh playwright.ps1 install chromium");

            _installAttempted = true;

            var script = Path.Combine(AppContext.BaseDirectory, "playwright.ps1");
            if (!File.Exists(script))
                throw new FileNotFoundException(
                    $"Playwright's install script was not found at '{script}'. Build the project, then run " +
                    "'pwsh playwright.ps1 install chromium' from the output folder.", script);

            var startInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(script);
            startInfo.ArgumentList.Add("install");
            startInfo.ArgumentList.Add("chromium");

            using var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) log?.Invoke(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log?.Invoke(e.Data); };

            if (!process.Start())
                throw new InvalidOperationException("Failed to start Playwright's browser install.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("Playwright's browser install did not finish within 10 minutes.");
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Playwright's browser install failed with exit code {process.ExitCode}.");
        }
        finally
        {
            InstallLock.Release();
        }
    }
}
