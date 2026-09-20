namespace SivayaanHMS.Automation;

/// <summary>
/// Runs the real SivayaanHMS.Api against a throwaway PostgreSQL database for the duration of a
/// run — the same "one process over real data, not a mock" choice TransTrack.Automation makes,
/// adapted for a backend that speaks to a database server rather than a file.
///
/// The database is created by the API itself: <c>Program.cs</c> migrates on startup under
/// <c>IsDevelopment()</c>, and <c>Database.MigrateAsync()</c> creates the target database from
/// nothing when it does not exist — Npgsql connects to the server's maintenance database and
/// issues the CREATE DATABASE. Pointing the configuration at a name nobody has used before gets
/// the same "a fresh, correctly-shaped database costs one process start" result
/// TransTrack.Automation gets from SQLite — provided the role the run signs in as is allowed to
/// CREATE DATABASE, which the development role in docs/POSTGRESQL_SETUP.md is and a production
/// role deliberately is not.
///
/// <b>Why this runs from a publish, not a build.</b> <c>appsettings.Local.json</c> holds a
/// developer's own database password, and <c>Program.cs</c> loads it last — deliberately, so it
/// overrides everything, including the <c>Database__*</c> environment variables set here. A plain
/// <c>dotnet build</c> still copies that file into <c>bin/</c>; a <c>dotnet publish</c> does not,
/// because a prior commit ("Stop the developer's secrets file from shipping to production") marked
/// it <c>CopyToPublishDirectory="Never"</c> for exactly this reason, on the production deployment
/// path. Running the UAT suite from the publish output means it inherits that same guarantee for
/// free: there is no file in the folder this process starts from that could override the
/// throwaway database with a developer's real one. Skipping this and running from
/// <c>bin/Debug</c> instead would silently point a UAT run at whatever database that developer's
/// machine is actually configured for.
/// </summary>
public sealed class ApiServer : IAsyncDisposable
{
    private readonly System.Diagnostics.Process? _ownedProcess;
    private readonly string? _databaseName;
    private readonly string _postgresServer;

    private ApiServer(string baseUrl, System.Diagnostics.Process? ownedProcess, string? databaseName, string postgresServer)
    {
        BaseUrl = baseUrl;
        _ownedProcess = ownedProcess;
        _databaseName = databaseName;
        _postgresServer = postgresServer;
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
            return new ApiServer(baseUrl, ownedProcess: null, databaseName: null, options.PostgresServer);
        }

        if (!options.ManageServers)
            throw new InvalidOperationException(
                $"Nothing is serving {baseUrl} and SIVAYAANHMS_UAT_MANAGE_SERVERS=false, so the automation " +
                "will not start one. Start the API yourself or unset that variable.");

        // Lower-case: PostgreSQL folds unquoted identifiers, and a name that needs quoting in every
        // psql command is a name somebody will eventually mistype while cleaning up.
        var databaseName = $"sivayaanhms_uat_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}";
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

        // Database:ConnectionString replaces every individual Database:* key in appsettings.json,
        // which is exactly what a run wants: the server and role from the options, the run's own
        // database name, and nothing inherited from the file.
        var connectionString =
            $"{options.PostgresServer.TrimEnd(';')};Database={databaseName};Application Name=SivayaanHMS-UAT";

        var environment = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development", // gates the startup auto-migrate in Program.cs
            ["ASPNETCORE_URLS"] = baseUrl,
            ["Database__ConnectionString"] = connectionString,
            ["Database__MigrateOnStartup"] = "true",

            // appsettings.json's default only allows http://localhost:5173 — the developer's own
            // Vite port, not the UAT's dedicated one.
            ["Cors__AllowedOrigins__0"] = options.BaseUrl.TrimEnd('/'),
        };

        log?.Invoke($"Starting SivayaanHMS.Api on {baseUrl} against throwaway database '{databaseName}' on {Describe(options.PostgresServer)}");

        var dllPath = Path.Combine(publishDir, "SivayaanHMS.Api.dll");
        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"Publish did not produce '{dllPath}'.", dllPath);

        var process = ManagedProcess.Start("dotnet", publishDir, new[] { dllPath }, environment);

        var server = new ApiServer(baseUrl, process, databaseName, options.PostgresServer);

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

    /// <summary>The server half of a connection string, for a log line — never the password.</summary>
    private static string Describe(string connectionString)
    {
        var kept = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.TrimStart().StartsWith("Password", StringComparison.OrdinalIgnoreCase));
        return string.Join(";", kept);
    }

    private static async Task PublishAsync(string outputDir, Action<string>? log)
    {
        Directory.CreateDirectory(outputDir);

        // Generous on purpose: the first publish of a run is a cold Release build and restore of
        // four projects, and this machine routinely has other builds, servers and a database
        // server itself competing for the same CPU and disk. Every publish after the first is
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
                "Program.cs and would override every database setting this automation sets, " +
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
        // ran on than against nothing. Unlike a stray file, a database does not just disappear
        // when a folder is cleaned up, so it is named clearly and logged so it can be found and
        // dropped — see this project's README for the sweep command.
        if (_databaseName is not null)
            Console.WriteLine(
                $"Left '{_databaseName}' on {Describe(_postgresServer)} for inspection. Drop it with:{Environment.NewLine}" +
                $"  psql -h localhost -U sivayaanhms -d postgres -c \"DROP DATABASE \\\"{_databaseName}\\\" WITH (FORCE);\"");
    }
}
