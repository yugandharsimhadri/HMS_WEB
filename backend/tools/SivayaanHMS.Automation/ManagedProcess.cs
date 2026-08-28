using System.Diagnostics;

namespace SivayaanHMS.Automation;

/// <summary>
/// Starting a long-running child process and knowing when it is ready is the same problem for
/// the API and for the Vite dev server, so it is solved once here — ported from
/// TransTrack.Automation's helper of the same name, which solves the same problem for
/// TransTrack.Api and the Next.js dev server.
/// </summary>
internal static class ManagedProcess
{
    private static readonly HttpClient Probe = new() { Timeout = TimeSpan.FromSeconds(3) };

    /// <summary>
    /// The tail of each child's output, keyed by the process. A server that dies during startup
    /// says why on its own stdout, and without this that message is discarded and the caller is
    /// left with "exited with code 1" — which names the symptom and nothing else.
    /// </summary>
    private static readonly Dictionary<int, Queue<string>> RecentOutput = [];

    private const int RecentOutputLines = 40;

    public static string TailOf(Process process)
    {
        lock (RecentOutput)
        {
            return RecentOutput.TryGetValue(process.Id, out var lines) && lines.Count > 0
                ? string.Join(Environment.NewLine, lines)
                : "(the process produced no output)";
        }
    }

    private static void Remember(int processId, string? line)
    {
        if (line is null)
            return;

        lock (RecentOutput)
        {
            if (!RecentOutput.TryGetValue(processId, out var lines))
                RecentOutput[processId] = lines = new Queue<string>();

            lines.Enqueue(line);
            while (lines.Count > RecentOutputLines)
                lines.Dequeue();
        }
    }

    /// <summary>
    /// dotnet and node are both fine to exec directly on Windows; npm and npx are .cmd shims that
    /// CreateProcess cannot execute, so those go through cmd.exe. Not used to launch Vite — see
    /// <see cref="WebDevServer"/> for why that runs node directly against Vite's own entry script
    /// instead.
    /// </summary>
    public static Process Start(
        string fileName,
        string workingDirectory,
        IEnumerable<string> arguments,
        IDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var needsShim = OperatingSystem.IsWindows() && fileName is "npm" or "npx";
        if (needsShim)
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(fileName);
        }
        else
        {
            startInfo.FileName = fileName;
        }

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        if (environment is not null)
            foreach (var (key, value) in environment)
                startInfo.Environment[key] = value;

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start '{fileName} {string.Join(' ', arguments)}'.");

        // Drained because a child blocks once its output buffer fills, which would strand the
        // server halfway through starting up — and kept, because the tail of it is the only
        // explanation available when startup fails.
        var id = process.Id;
        process.OutputDataReceived += (_, e) => Remember(id, e.Data);
        process.ErrorDataReceived += (_, e) => Remember(id, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    /// <summary>Runs a short-lived command to completion and returns its exit code and combined output.</summary>
    public static async Task<(int ExitCode, string Output)> RunToCompletionAsync(
        string fileName,
        string workingDirectory,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        IDictionary<string, string>? environment = null)
    {
        var process = Start(fileName, workingDirectory, arguments, environment);

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException(
                $"'{fileName} {string.Join(' ', arguments)}' did not finish within {timeout.TotalSeconds:0}s." +
                Environment.NewLine + Environment.NewLine + TailOf(process));
        }

        var output = TailOf(process);
        var exitCode = process.ExitCode;
        process.Dispose();
        return (exitCode, output);
    }

    /// <summary>Polls until the URL answers, failing early if the process dies first.</summary>
    public static async Task WaitUntilRespondingAsync(
        string url,
        TimeSpan timeout,
        Process process,
        string what,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
                throw new InvalidOperationException(
                    $"{what} exited early with code {process.ExitCode} before serving {url}." +
                    Environment.NewLine + Environment.NewLine + TailOf(process));

            if (await IsRespondingAsync(url, cancellationToken))
                return;

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException(
            $"{what} did not start serving {url} within {timeout.TotalSeconds:0}s." +
            Environment.NewLine + Environment.NewLine + TailOf(process));
    }

    /// <summary>
    /// Any answer at all counts, including 4xx — an API that returns 404 for "/" is up, and the
    /// point of the probe is liveness, not correctness.
    /// </summary>
    public static async Task<bool> IsRespondingAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Probe.GetAsync(url, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Kills the whole tree — dotnet and node both spawn children that would keep the port bound.</summary>
    public static async Task StopAsync(Process? process)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch
        {
            // Best effort — a leaked server is reused by the next run anyway.
        }
        finally
        {
            process.Dispose();
        }
    }
}
