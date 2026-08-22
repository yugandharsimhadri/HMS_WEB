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
    /// <summary>The part before the clinic — the seeded account is
    /// "admin@your-clinic", not a bare "Admin".</summary>
    public const string DefaultAdminLocalPart = "admin";

    private const string DefaultAdminPassword = "HMSAdmin@123";

    /// <summary>
    /// Seeds the one account every clinic starts with. The username is
    /// scoped to the clinic's own slug, so it is unique across the whole
    /// platform and sign-in can resolve the clinic from it — see
    /// <see cref="UserName"/>.
    /// </summary>
    public static async Task<User> SeedAsync(
        AppDbContext db, string clinicSlug, ILogger logger, CancellationToken ct = default)
    {
        var username = UserName.For(DefaultAdminLocalPart, clinicSlug);

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (existing is not null) return existing;

        var (hash, salt) = PasswordHasher.Hash(DefaultAdminPassword);

        var admin = new User
        {
            Username = username,
            DisplayName = "Admin",
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = UserRole.Admin,
            IsActive = true,
            MustChangePassword = true
        };

        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded the default admin account {Username}.", username);
        return admin;
    }
}
