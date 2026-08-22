using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SivayaanHMS.Core;
using SivayaanHMS.Data.Security;

namespace SivayaanHMS.Data;

public enum LoginOutcome
{
    Success,
    EnterpriseRecovery,
    Failed
}

/// <summary>Result of a login attempt. EnterpriseRecovery is not a normal
/// sign-in — the caller must route it to the password-reset screen, never to
/// the application shell.</summary>
public record LoginResult(LoginOutcome Outcome, User? User, string? Message)
{
    public static LoginResult Success(User user) => new(LoginOutcome.Success, user, null);
    public static LoginResult EnterpriseRecovery() => new(LoginOutcome.EnterpriseRecovery, null, null);
    public static LoginResult Failed(string message) => new(LoginOutcome.Failed, null, message);
}

/// <summary>
/// Sign-in, password changes, and the user list Admin manages. Everything
/// here is inert unless Settings → Security has "Require login" switched on;
/// nothing in this class enforces that — the caller decides whether to show
/// the login screen at all.
///
/// Tenant resolution happens upstream of this class, not inside it: by the
/// time LoginAsync runs, the API layer has already worked out which clinic
/// this request is for (subdomain, clinic code, whatever the login page
/// asks for) and built <paramref name="factory"/>'s AppDbContext with that
/// tenant already set on ICurrentTenantContext. Username lookups here are
/// then automatically scoped to that one tenant by AppDbContext's global
/// filter — the same "Admin" username two different clinics each seeded for
/// themselves resolves to two different users, correctly, without this
/// class needing to know that.
/// </summary>
public class AuthService(IDbContextFactory<AppDbContext> factory, IClock clock, ILogger<AuthService> logger)
{
    /// <summary>
    /// The support/recovery identity. Never a row in the Users table — a
    /// constant checked directly here — so it can never be listed, edited,
    /// renamed or deleted from any screen, and its password never changes
    /// through this application. Known only to the people who build and
    /// support this software; a clinic's own Admin cannot see or use it.
    ///
    /// TODO(SaaS): on the desktop this was a fixed recovery door into the
    /// one clinic on the machine. On a multi-tenant server it needs to carry
    /// (or be handed) which tenant it is recovering — today it resolves
    /// within whatever tenant the caller's AppDbContext already points at,
    /// same as every other login. Revisit before this ships as a real
    /// support workflow.
    /// </summary>
    public const string EnterpriseAdminUsername = "EnterpriseAdmin";

    private static readonly Lazy<(string Hash, string Salt)> EnterpriseAdminCredential =
        new(() => PasswordHasher.Hash("SivAyAAn@HMS"));

    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        username = username?.Trim() ?? string.Empty;

        if (string.Equals(username, EnterpriseAdminUsername, StringComparison.OrdinalIgnoreCase))
        {
            var (hash, salt) = EnterpriseAdminCredential.Value;
            if (!PasswordHasher.Verify(password, hash, salt))
                return LoginResult.Failed("Incorrect username or password.");

            logger.LogInformation("EnterpriseAdmin signed in for password recovery.");
            return LoginResult.EnterpriseRecovery();
        }

        await using var db = await factory.CreateDbContextAsync();

        // Case-insensitive on purpose — "Admin" and "admin" are the same
        // account. .ToLower() on both sides (not .ToLowerInvariant()) is
        // what EF Core actually translates to a SQL LOWER() comparison.
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Username.ToLower() == username.ToLower() && !u.IsDeleted);

        if (user is null || !user.IsActive || !PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt))
            return LoginResult.Failed("Incorrect username or password.");

        user.LastLoginOn = clock.Now;
        await db.SaveChangesAsync();

        return LoginResult.Success(user);
    }

    public async Task<List<User>> GetUsersAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Users.AsNoTracking()
            .Where(u => !u.IsDeleted)
            .OrderBy(u => u.Username)
            .ToListAsync();
    }

    /// <summary>Creates a user, or updates one when <paramref name="user"/>.Id
    /// matches an existing row. A new user always needs a password; an
    /// existing one keeps its current password unless <paramref name="newPassword"/>
    /// is supplied. Either way, setting a password here always requires a
    /// change on next login — never set silently for somebody else.</summary>
    public async Task SaveUserAsync(User user, string? newPassword)
    {
        var username = user.Username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("Username is required.");
        if (string.Equals(username, EnterpriseAdminUsername, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"'{EnterpriseAdminUsername}' is reserved.");

        await using var db = await factory.CreateDbContextAsync();

        // IgnoreQueryFilters: a username is unique across the whole platform
        // now, not within one clinic (see UserName), so the check has to see
        // past this tenant's filter — otherwise it would happily hand out a
        // name another clinic already holds and the unique index would throw
        // instead, with a message nobody can act on.
        if (await db.Users.IgnoreQueryFilters()
                .AnyAsync(u => u.Username.ToLower() == username.ToLower() && !u.IsDeleted && u.Id != user.Id))
            throw new InvalidOperationException($"'{username}' is already in use.");

        var entity = user.Id == Guid.Empty ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
        var isNew = entity is null;
        entity ??= new User();

        entity.Username = username;
        entity.DisplayName = (user.DisplayName ?? string.Empty).Trim();
        entity.Role = user.Role;
        entity.IsActive = user.IsActive;

        if (isNew && string.IsNullOrWhiteSpace(newPassword))
            throw new InvalidOperationException("A password is required for a new user.");

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            var (hash, salt) = PasswordHasher.Hash(newPassword);
            entity.PasswordHash = hash;
            entity.PasswordSalt = salt;
            entity.MustChangePassword = true;
        }

        if (isNew) db.Users.Add(entity);

        await db.SaveChangesAsync();
    }

    /// <summary>The signed-in user setting their own new password, typically
    /// right after signing in with a temporary one.</summary>
    public async Task ChangeOwnPasswordAsync(Guid userId, string newPassword)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId)
                    ?? throw new InvalidOperationException("User not found.");

        var (hash, salt) = PasswordHasher.Hash(newPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.MustChangePassword = false;

        await db.SaveChangesAsync();
    }

    /// <summary>EnterpriseAdmin resetting a locked-out user's password —
    /// reached only after an EnterpriseRecovery login, never from inside the
    /// application shell. Always leaves MustChangePassword set, so the
    /// temporary password handed out here is only ever good for one sign-in.</summary>
    public async Task<string> ResetPasswordAsync(Guid userId, string temporaryPassword)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId)
                    ?? throw new InvalidOperationException("User not found.");

        var (hash, salt) = PasswordHasher.Hash(temporaryPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.MustChangePassword = true;

        await db.SaveChangesAsync();

        logger.LogInformation("Password reset via EnterpriseAdmin for user '{Username}'.", user.Username);
        return user.Username;
    }
}
