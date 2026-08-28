namespace SivayaanHMS.Automation;

/// <summary>
/// Locates the repository layout at runtime, the same problem
/// TransTrack.Automation and ABPS_WEB.Automation solve on this machine and
/// the same answer: walk up from the running assembly until the marker
/// appears, because nothing here can trust
/// <see cref="Environment.CurrentDirectory"/> — a test runner or a CI agent
/// launches from wherever it pleases.
///
/// The marker is the pair of folders every checkout has (<c>backend</c> and
/// <c>frontend</c> siblings) rather than a single solution file: HMS_WEB's
/// solution lives at <c>backend/SivayaanHMS.slnx</c>, one level short of the
/// root this class actually needs, since the frontend the UAT drives sits
/// beside <c>backend</c>, not inside it.
/// </summary>
public static class RepoPaths
{
    private static readonly Lazy<string> RootLazy = new(FindRoot);

    /// <summary>Absolute path to the repository root — the folder holding both <c>backend</c> and <c>frontend</c>.</summary>
    public static string Root => RootLazy.Value;

    /// <summary>Absolute path to the API project, published fresh against a throwaway database for each run.</summary>
    public static string ApiProject => Path.Combine(Root, "backend", "src", "SivayaanHMS.Api", "SivayaanHMS.Api.csproj");

    /// <summary>Absolute path to the React client Vite serves.</summary>
    public static string WebProject => Path.Combine(Root, "frontend");

    /// <summary>Where screenshots, the throwaway API publish output, and anything else a run produces are written.</summary>
    public static string ArtifactsDir => Path.Combine(Root, "backend", "artifacts", "uat");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "backend")) &&
                Directory.Exists(Path.Combine(dir.FullName, "frontend")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the HMS_WEB repository root (a folder containing both 'backend' and " +
            $"'frontend') walking up from '{AppContext.BaseDirectory}'. Set SIVAYAANHMS_UAT_WEB_PATH " +
            "to the absolute path of the frontend project to bypass repo discovery.");
    }
}
