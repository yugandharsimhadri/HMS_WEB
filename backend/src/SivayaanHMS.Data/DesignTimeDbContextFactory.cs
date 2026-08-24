using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SivayaanHMS.Data;

/// <summary>
/// Used only by `dotnet ef`. Adding a migration needs no reachable database —
/// the tools read the model, not a server — so the connection string here
/// only has to name the right provider for the SQL that gets generated.
///
/// Override it when a command really does need to reach the database, such as
/// `database update` or `migrations script --from`:
///
///     $env:SIVAYAANHMS_CONNECTION = "Server=.\SQLEXPRESS;Database=HMSLite;Trusted_Connection=True;TrustServerCertificate=True"
///     dotnet ef migrations list --project src\SivayaanHMS.Data --startup-project src\SivayaanHMS.Data
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public const string ConnectionOverrideVariable = "SIVAYAANHMS_CONNECTION";

    /// <summary>A name that resolves nowhere in particular. Generating a
    /// migration never opens it; anything that does is told to set the
    /// variable above.</summary>
    private const string DesignTimeFallback =
        "Server=(local);Database=SivayaanHMS_DesignTime;Trusted_Connection=True;TrustServerCertificate=True";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionOverrideVariable);
        if (string.IsNullOrWhiteSpace(connection)) connection = DesignTimeFallback;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection)
            .Options;

        return new AppDbContext(options);
    }
}
