using System.Net;
using System.Net.Sockets;

namespace SivayaanHMS.Automation;

/// <summary>
/// Brings up the Vite dev server if nothing is already serving it. An already-running server is
/// reused and never shut down, so a developer with <c>npm run dev</c> open in another terminal
/// keeps it after a run finishes — the same policy TransTrack.Automation's equivalent class
/// applies to its Next.js dev server.
///
/// Simpler than that equivalent in one real way: Vite has a native <c>--strictPort</c> flag, so
/// there is no need to reimplement Next's silent-fallback-to-the-next-free-port behaviour by hand.
/// </summary>
public sealed class WebDevServer : IAsyncDisposable
{
    private readonly System.Diagnostics.Process? _ownedProcess;

    private WebDevServer(System.Diagnostics.Process? ownedProcess) => _ownedProcess = ownedProcess;

    public static async Task<WebDevServer> StartAsync(
        AutomationOptions options,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = options.BaseUrl.TrimEnd('/');

        if (await ManagedProcess.IsRespondingAsync(baseUrl, cancellationToken))
        {
            // Something answers, but "a Vite dev server" is not the same as "*our* client" —
            // several projects on this machine could be mid-`npm run dev`. Identified before it
            // is trusted, so attaching to the wrong one fails with a clear sentence rather than as
            // a string of mysterious selector timeouts that read like SivayaanHMS bugs.
            if (!await IsServingSivayaanHmsAsync(baseUrl, cancellationToken))
                throw new InvalidOperationException(
                    $"{baseUrl} is already serving a different application, not the SivayaanHMS client. " +
                    "Stop whatever is on that port, or point the suite elsewhere with SIVAYAANHMS_UAT_BASE_URL.");

            log?.Invoke($"Reusing the SivayaanHMS dev server already serving {baseUrl}");
            return new WebDevServer(ownedProcess: null);
        }

        if (!options.ManageServers)
            throw new InvalidOperationException(
                $"Nothing is serving {baseUrl} and SIVAYAANHMS_UAT_MANAGE_SERVERS=false, so the automation " +
                "will not start one. Start the client yourself or unset that variable.");

        var webPath = options.WebProjectPath;
        if (!Directory.Exists(webPath))
            throw new DirectoryNotFoundException($"Client project not found at '{webPath}'.");

        await EnsureNodeModulesAsync(webPath, log, cancellationToken);

        var port = new Uri(baseUrl).Port;

        // Belt and braces alongside --strictPort below: this turns a busy port into the sentence
        // a person actually needs before Vite even starts, rather than a startup failure buried
        // in the child's own stdout.
        EnsurePortAvailable(port);

        var viteBin = Path.Combine(webPath, "node_modules", "vite", "bin", "vite.js");
        if (!File.Exists(viteBin))
            throw new FileNotFoundException(
                $"Vite is not installed at '{viteBin}'. Run 'npm install' in {webPath}.", viteBin);

        log?.Invoke($"Starting the Vite dev server in {webPath} on port {port}");

        var process = ManagedProcess.Start(
            "node",
            webPath,
            new[] { viteBin, "--port", port.ToString(), "--strictPort" },
            new Dictionary<string, string>
            {
                // The whole reason the dev server is started here rather than by hand: the client
                // must call the run's own throwaway API, not whatever a developer's
                // .env.development happens to point at. Vite exposes any VITE_-prefixed variable
                // already present in process.env through import.meta.env, and a real process.env
                // value takes priority over one loaded from a .env file — so this genuinely
                // overrides frontend/.env.development's http://localhost:5130.
                ["VITE_API_URL"] = options.ApiBaseUrl.TrimEnd('/'),
            });

        var server = new WebDevServer(process);

        try
        {
            await ManagedProcess.WaitUntilRespondingAsync(
                baseUrl, TimeSpan.FromSeconds(60), process, "The Vite dev server", cancellationToken);
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }

        log?.Invoke($"Dev server ready at {baseUrl}");
        return server;
    }

    /// <summary>
    /// Refuses to start when something already holds the port that is not answering HTTP (a
    /// non-web listener, or one that happened not to answer when probed a moment ago).
    /// </summary>
    private static void EnsurePortAvailable(int port)
    {
        try
        {
            // IPv6Any in dual-mode: a port bound only on the v4 loopback would still report free
            // to a v4-only probe while something is listening on the v6 side, or vice versa.
            using var listener = new TcpListener(IPAddress.IPv6Any, port);
            listener.Server.DualMode = true;
            listener.Start();
        }
        catch (SocketException)
        {
            throw new InvalidOperationException(
                $"Port {port} is already in use, so the SivayaanHMS dev server cannot have it. " +
                "Stop whatever is holding it, or point the suite at another port with SIVAYAANHMS_UAT_BASE_URL.");
        }
    }

    private static async Task EnsureNodeModulesAsync(string webPath, Action<string>? log, CancellationToken cancellationToken)
    {
        if (Directory.Exists(Path.Combine(webPath, "node_modules")))
            return;

        log?.Invoke("node_modules is missing — running 'npm install' (first run only, this takes a while)");

        var (exitCode, output) = await ManagedProcess.RunToCompletionAsync(
            "npm", webPath, new[] { "install" }, TimeSpan.FromMinutes(5));

        if (exitCode != 0)
            throw new InvalidOperationException($"'npm install' failed in '{webPath}' with exit code {exitCode}:{Environment.NewLine}{output}");

        _ = cancellationToken; // npm install is not itself cancellable through this helper; the caller's own timeout still applies to the whole StartAsync.
    }

    /// <summary>
    /// Identifies the app by content on its served <c>index.html</c> rather than by a client-side
    /// route: Vite's dev server always serves the same shell at "/", regardless of what
    /// server-side history-fallback routing is or is not configured for deeper paths, so probing
    /// "/" is the one request guaranteed to work the same way whatever the router setup.
    /// </summary>
    private static async Task<bool> IsServingSivayaanHmsAsync(string baseUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var body = await http.GetStringAsync($"{baseUrl}/", cancellationToken);
            return body.Contains("Sivayaan HMS", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public ValueTask DisposeAsync() => new(ManagedProcess.StopAsync(_ownedProcess));
}
