using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SivayaanHMS.Core;
using SivayaanHMS.Data.Security;

namespace SivayaanHMS.Data;

/// <summary>
/// Seeds the one account every tenant needs regardless of whether login is
/// switched on: "Admin", so turning Settings → Security → Require login on
/// is immediately usable without a separate setup step. Never overwrites —
/// a password Admin has since changed, or a user Admin has since renamed,
/// survives every later run.
///
/// On the web edition this runs once, during clinic signup, against a
/// db already scoped to the brand-new tenant — AppDbContext stamps
/// TenantId on the row it adds from that context, same as every other save.
/// </summary>
public static class UserSeeder
{
    public const string DefaultAdminUsername = "Admin";
    private const string DefaultAdminPassword = "HMSAdmin@123";

    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(u => u.Username == DefaultAdminUsername, ct)) return;

        var (hash, salt) = PasswordHasher.Hash(DefaultAdminPassword);

        db.Users.Add(new User
        {
            Username = DefaultAdminUsername,
            DisplayName = "Admin",
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = UserRole.Admin,
            IsActive = true,
            MustChangePassword = true
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded the default Admin account.");
    }
}
