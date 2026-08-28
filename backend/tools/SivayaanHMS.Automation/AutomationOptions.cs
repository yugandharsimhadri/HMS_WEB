namespace SivayaanHMS.Automation;

/// <summary>
/// All knobs the automation reads, resolved from environment variables so a CI runner can steer
/// a run without a rebuild. Every value has a working default, so a bare <c>dotnet test</c> does
/// the right thing with no configuration at all — the same contract TransTrack.Automation and
/// ABPS_WEB.Automation make on this machine.
///
/// Unlike those two, there is no <c>RunMode</c> or <c>Viewport</c> here. Both exist there because
/// the same workflow objects also drive a video recorder, and TransTruck has a genuinely
/// different mobile layout (a bottom tab bar with screens filed behind "More") worth capturing on
/// its own. Neither is true of SivayaanHMS today — nothing records video, and the whole product is
/// desktop-first with one responsive breakpoint that reflows two CSS properties. Inventing a
/// distinction the product does not have would test a UI that does not exist. Add it back if a
/// mobile layout ever does.
/// </summary>
public sealed record AutomationOptions
{
    /// <summary>
    /// Where the Vite client is served from. Deliberately not Vite's own default 5173 — that is
    /// the port a developer's own <c>npm run dev</c> is likely already using, and a run that
    /// silently attached to it would drive whatever that developer happened to have open. The dev
    /// server is started with <c>--strictPort</c> on this port, so a clash fails loudly instead of
    /// hopping to the next free one.
    /// </summary>
    public string BaseUrl { get; init; } = "http://localhost:5410";

    /// <summary>
    /// Where the API is served from, and what <c>VITE_API_URL</c> is set to for the dev server
    /// this automation starts. 5411 for the same reason as 5410 — the API's own development port
    /// is 5130 and its production port is 6051, and both are deliberately left alone so a UAT run
    /// can never reach a real installation.
    /// </summary>
    public string ApiBaseUrl { get; init; } = "http://localhost:5411";

    /// <summary>
    /// The SQL Server instance a throwaway database is created on for the run, in the form
    /// <c>sqlcmd -S</c> takes: <c>.\INSTANCE</c> or <c>(localdb)\MSSQLLocalDB</c>. LocalDB by
    /// default — it needs no service to be pre-started and starts itself on first connection,
    /// which is the lowest-friction thing that works on a checkout with nothing configured yet.
    /// Point this at SQLEXPRESS, or wherever the product actually runs, to test against the same
    /// engine production uses.
    /// </summary>
    public string SqlServerInstance { get; init; } = @"(localdb)\MSSQLLocalDB";

    /// <summary>Absolute path to the frontend project, used to start the Vite dev server on demand.</summary>
    public string WebProjectPath { get; init; } = "";

    /// <summary>
    /// When false the automation assumes something else already serves <see cref="BaseUrl"/> and
    /// <see cref="ApiBaseUrl"/>, and will neither start nor stop them, nor create a database.
    /// Useful when pointing the suite at an already-running pair while writing a workflow — start
    /// both by hand once, then iterate on a single test without paying the publish-and-migrate
    /// cost on every run.
    /// </summary>
    public bool ManageServers { get; init; } = true;

    /// <summary>
    /// Skips <c>dotnet publish</c> and reuses whatever is already in the publish output folder
    /// from a previous run. The build is what makes a run slow; this is for iterating on a
    /// workflow's Playwright steps without waiting for it every time the app itself has not
    /// changed. Has no effect unless a previous run already populated the folder.
    /// </summary>
    public bool SkipApiPublish { get; init; }

    /// <summary>
    /// Builds the options from environment variables, falling back to the defaults above:
    /// SIVAYAANHMS_UAT_BASE_URL, SIVAYAANHMS_UAT_API_BASE_URL, SIVAYAANHMS_UAT_SQL_INSTANCE,
    /// SIVAYAANHMS_UAT_WEB_PATH, SIVAYAANHMS_UAT_MANAGE_SERVERS (true|false),
    /// SIVAYAANHMS_UAT_SKIP_API_PUBLISH (true|false).
    /// </summary>
    public static AutomationOptions FromEnvironment() => new()
    {
        BaseUrl = Env("SIVAYAANHMS_UAT_BASE_URL") ?? "http://localhost:5410",
        ApiBaseUrl = Env("SIVAYAANHMS_UAT_API_BASE_URL") ?? "http://localhost:5411",
        SqlServerInstance = Env("SIVAYAANHMS_UAT_SQL_INSTANCE") ?? @"(localdb)\MSSQLLocalDB",
        WebProjectPath = Env("SIVAYAANHMS_UAT_WEB_PATH") ?? RepoPaths.WebProject,
        ManageServers = !string.Equals(Env("SIVAYAANHMS_UAT_MANAGE_SERVERS"), "false", StringComparison.OrdinalIgnoreCase),
        SkipApiPublish = string.Equals(Env("SIVAYAANHMS_UAT_SKIP_API_PUBLISH"), "true", StringComparison.OrdinalIgnoreCase),
    };

    private static string? Env(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
