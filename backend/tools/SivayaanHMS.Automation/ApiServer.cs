namespace SivayaanHMS.Automation;

/// <summary>
/// Runs the real SivayaanHMS.Api against a throwaway SQL Server database for the duration of a
/// run — the same "one process over real data, not a mock" choice TransTrack.Automation makes,
/// adapted for a backend that speaks SQL Server rather than a file.
///
/// The database is created by the API itself: <c>Program.cs</c> already migrates on startup under
/// <c>IsDevelopment()</c>, and <c>Database.MigrateAsync()</c> creates the target database from
/// nothing when it does not exist. Pointing the connection string at a name nobody has used before
/// gets the same "a fresh, correctly-shaped database costs one process start" result
/// TransTrack.Automation gets from SQLite — just with an extra step, because unlike a SQLite file
/// this only works if something is allowed to <c>CREATE DATABASE</c>, which Windows-authenticated
/// LocalDB grants by default and a production login deliberately would not.
///
/// <b>Why this runs from a publish, not a build.</b> <c>appsettings.Local.json</c> holds a
/// developer's own SQL password, and <c>Program.cs</c> loads it last — deliberately, so it
/// overrides everything, including a <c>ConnectionStrings__Default</c> environment variable set
/// here. A plain <c>dotnet build</c> still copies that file into <c>bin/</c>; a
/// <c>dotnet publish</c> does not, because a prior commit
/// ("Stop the developer's secrets file from shipping to production") marked it
/// <c>CopyToPublishDirectory="Never"</c> for exactly this reason, on the production deployment
/// path. Running the UAT suite from the publish output means it inherits that same guarantee for
/// free: there is no file in the folder this process starts from that could override the
/// throwaway connection string with a developer's real one. Skipping this and running from
/// <c>bin/Debug</c> instead would silently point a UAT run at whatever database that developer's
/// machine is actually configured for.
/// </summary>
public sealed class ApiServer : IAsyncDisposable
{
    private readonly System.Diagnostics.Process? _ownedProcess;
    private readonly string? _databaseName;
    private readonly string _sqlInstance;

    private ApiServer(string baseUrl, System.Diagnostics.Process? ownedProcess, string? databaseName, string sqlInstance)
    {
        BaseUrl = baseUrl;
        _ownedProcess = ownedProcess;
        _databaseName = databaseName;
        _sqlInstance = sqlInstance;
    }

    public string BaseUrl { get; }

    /// <summary>
    /// Starts the API on <see cref="AutomationOptions.ApiBaseUrl"/> against a fresh database,
    /// unless something is already answering there — in which case it is reused and left running,
    /// so a developer with the API up in another terminal (see
    /// <see cref="AutomationOptions.ManageServers"/>) keeps it.
    /// </summary>
    public static async Task<ApiServer> StartAsync(
        AutomationOptions options,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = options.ApiBaseUrl.TrimEnd('/');

        if (await ManagedProcess.IsRespondingAsync($"{baseUrl}/", cancellationToken))
        {
            log?.Invoke($"Reusing whatever is already serving {baseUrl}");
            return new ApiServer(baseUrl, ownedProcess: null, databaseName: null, options.SqlServerInstance);
        }

        if (!options.ManageServers)
            throw new InvalidOperationException(
                $"Nothing is serving {baseUrl} and SIVAYAANHMS_UAT_MANAGE_SERVERS=false, so the automation " +
                "will not start one. Start the API yourself or unset that variable.");

        var databaseName = $"SivayaanHMSUat_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}";
        var publishDir = Path.Combine(RepoPaths.ArtifactsDir, "api-publish");

        if (!options.SkipApiPublish || !File.Exists(Path.Combine(publishDir, "SivayaanHMS.Api.dll")))
        {
            log?.Invoke($"Publishing SivayaanHMS.Api to {publishDir}");
            await PublishAsync(publishDir, log);
        }
        else
        {
            log?.Invoke($"Reusing the existing publish at {publishDir} (SIVAYAANHMS_UAT_SKIP_API_PUBLISH=true)");
        }

        // Trusted_Connection: the same reason SQL_SERVER_SETUP.md gives it as the deployment
        // default — no password to store, leak or find in a log line for a process that only ever
        // exists for the lifetime of one test run.
        var connectionString =
            $@"Server={options.SqlServerInstance};Database={databaseName};Trusted_Connection=True;" +
            "TrustServerCertificate=True;MultipleActiveResultSets=False;Application Name=SivayaanHMS-UAT";

        var environment = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development", // gates the startup auto-migrate in Program.cs
            ["ASPNETCORE_URLS"] = baseUrl,
            ["ConnectionStrings__Default"] = connectionString,

            // appsettings.json's default only allows http://localhost:5173 — the developer's own
            // Vite port, not the UAT's dedicated one.
            ["Cors__AllowedOrigins__0"] = options.BaseUrl.TrimEnd('/'),
        };

        log?.Invoke($"Starting SivayaanHMS.Api on {baseUrl} against throwaway database '{databaseName}' on {options.SqlServerInstance}");

        var dllPath = Path.Combine(publishDir, "SivayaanHMS.Api.dll");
        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"Publish did not produce '{dllPath}'.", dllPath);

        var process = ManagedProcess.Start("dotnet", publishDir, new[] { dllPath }, environment);

        var server = new ApiServer(baseUrl, process, databaseName, options.SqlServerInstance);

        try
        {
            // Generous: the first request against a brand-new database applies every migration
            // before Kestrel starts accepting connections at all.
            await ManagedProcess.WaitUntilRespondingAsync(
                $"{baseUrl}/", TimeSpan.FromSeconds(90), process, "SivayaanHMS.Api", cancellationToken);
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }

        log?.Invoke($"API ready at {baseUrl}");
        return server;
    }

    private static async Task PublishAsync(string outputDir, Action<string>? log)
    {
        Directory.CreateDirectory(outputDir);

        // Generous on purpose: the first publish of a run is a cold Release build and restore of
        // four projects, and this machine routinely has other builds, servers and SQL Server
        // itself competing for the same CPU and disk. Every publish after the first is
        // incremental and finishes in a few seconds — see SIVAYAANHMS_UAT_SKIP_API_PUBLISH to
        // skip it entirely while iterating on a workflow.
        var (exitCode, output) = await ManagedProcess.RunToCompletionAsync(
            "dotnet",
            RepoPaths.Root,
            new[] { "publish", RepoPaths.ApiProject, "-c", "Release", "-o", outputDir, "--nologo" },
            TimeSpan.FromMinutes(10),
            new Dictionary<string, string>
            {
                // MSBuild persists worker nodes across separate `dotnet` invocations to speed up
                // a series of builds — normally a good trade, but this machine has had a great
                // many overlapping dotnet build/publish/test invocations across one long working
                // session, and a persisted node from an unrelated one of those can leave the
                // *next* invocation waiting on a lock that never clears, which reads from the
                // outside as this publish simply hanging. Disabling reuse for this one call costs
                // a little process-startup overhead and buys a publish that cannot be left in a
                // bad state by whatever else has touched this project tree recently.
                ["MSBUILDDISABLENODEREUSE"] = "1",
            });

        if (exitCode != 0)
            throw new InvalidOperationException($"'dotnet publish' failed with exit code {exitCode}:{Environment.NewLine}{output}");

        // The one guarantee this whole class depends on. Checked here rather than assumed, so a
        // future change to the .csproj that quietly drops the CopyToPublishDirectory="Never"
        // guard fails a UAT run immediately and loudly, instead of silently pointing it at
        // whichever database a developer's appsettings.Local.json happens to name.
        if (File.Exists(Path.Combine(outputDir, "appsettings.Local.json")))
            throw new InvalidOperationException(
                $"'{outputDir}' contains appsettings.Local.json. That file is loaded last by " +
                "Program.cs and would override every connection string this automation sets, " +
                "including the throwaway database — a UAT run must never be able to reach a real " +
                "one. It should be excluded from the publish output by " +
                "SivayaanHMS.Api.csproj's CopyToPublishDirectory=\"Never\" rule; that rule has " +
                "regressed or been removed.");

        log?.Invoke("Publish complete.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownedProcess is null)
            return;

        await ManagedProcess.StopAsync(_ownedProcess);

        // The database is left in place, exactly as TransTrack.Automation leaves its throwaway
        // SQLite file: a failed scenario is far easier to diagnose against the data it actually
        // ran on than against nothing. Unlike a stray file, a LocalDB/Express database does not
        // just disappear when a folder is cleaned up, so it is named clearly and logged so it can
        // be found and dropped — see docs in this project's README for the sweep command.
        if (_databaseName is not null)
            Console.WriteLine(
                $"Left '{_databaseName}' on {_sqlInstance} for inspection. Drop it with:{Environment.NewLine}" +
                $"  sqlcmd -S \"{_sqlInstance}\" -E -C -Q \"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];\"");
    }
}
