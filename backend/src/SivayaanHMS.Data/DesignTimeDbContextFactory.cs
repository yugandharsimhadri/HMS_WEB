using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SivayaanHMS.Data;

/// <summary>
/// Used only by `dotnet ef`. Adding a migration needs no real database — the
/// tools read the model, not a file — so this defaults to a throwaway one.
///
///     $env:SIVAYAANHMS_DB = "path\to\a\copy.db"
///     dotnet ef migrations list --project src\SivayaanHMS.Data --startup-project src\SivayaanHMS.Data
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public const string PathOverrideVariable = "SIVAYAANHMS_DB";

    public AppDbContext CreateDbContext(string[] args)
    {
        var path = Environment.GetEnvironmentVariable(PathOverrideVariable);
        if (string.IsNullOrWhiteSpace(path)) path = "design.db";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;

        return new AppDbContext(options);
    }
}
