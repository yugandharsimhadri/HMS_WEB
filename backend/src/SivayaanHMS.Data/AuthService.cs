using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SivayaanHMS.Core;
using SivayaanHMS.Data.Security;

namespace SivayaanHMS.Data;

public enum LoginOutcome
{
    Success,
    Failed
}

/// <summary>Result of a sign-in by one clinic's user. The platform support
/// identity never reaches this class — it belongs to no clinic, so it is
/// checked before a tenant has even been resolved (see PlatformAdminService
/// in the API layer).</summary>
public record LoginResult(LoginOutcome Outcome, User? User, string? Message)
{
    public static LoginResult Success(User user) => new(LoginOutcome.Success, user, null);
    public static LoginResult Failed(string message) => new(LoginOutcome.Failed, null, message);
}

/// <summary>
/// Sign-in, password changes, and the user list Admin manages.
///
/// Signing in is not optional on this edition. The desktop had a "Require
/// login" switch because one clinic ran it on one PC and could reasonably
/// leave it open; a multi-tenant server cannot, since the tenant is resolved
/// from the token and there is no request without one.
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
public class AuthService(IDbContextFactory<AppDbContext> factory, IClock clock)
{
    /// <summary>
    /// The support identity's name, kept here only so a clinic can never
    /// create a user that shadows it. It is not authenticated by this class:
    /// it belongs to no clinic, carries no tenant, and is checked before a
    /// tenant is resolved at all — see PlatformAdminService in the API
    /// layer, which also holds its (configured, not compiled-in) password.
    ///
    /// A clinic's own Admin cannot see, use, or create it.
    /// </summary>
    public const string EnterpriseAdminUsername = "EnterpriseAdmin";

    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        username = username?.Trim() ?? string.Empty;

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

        // The *local part*, not the whole username. This guard used to compare
        // the whole thing, which stopped meaning anything the moment usernames
        // gained their "@clinic" suffix — "enterpriseadmin@twinkle" sailed
        // past a check looking for exactly "EnterpriseAdmin".
        //
        // It grants nothing (support is matched on the bare name, before any
        // tenant is resolved), but a clinic account wearing that name exists
        // only to be mistaken for the support one by the staff who see it.
        var reservedPart = UserName.TrySplit(username, out var localPart, out _) ? localPart : username;

        if (string.Equals(reservedPart, EnterpriseAdminUsername, StringComparison.OrdinalIgnoreCase))
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

        // Where this person's password-reset code is sent. Copied like the
        // rest: SaveUserAsync builds the stored row from the one handed in,
        // and a field missed here is a field that silently never saves.
        entity.Phone = user.Phone;

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

    /// <summary>
    /// The signed-in user setting their own new password, having proved they
    /// know the current one.
    ///
    /// The current-password check is not ceremony: the commonest path here is
    /// straight after signing in with a temporary password that a support
    /// agent read out loud, and an unauthenticated-in-practice endpoint that
    /// changes passwords is worth more to an attacker than the session it
    /// sits behind. Returns false rather than throwing, because "you typed
    /// your old password wrong" is an ordinary outcome, not a fault.
    /// </summary>
    public async Task<bool> ChangeOwnPasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted)
                    ?? throw new InvalidOperationException("User not found.");

        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash, user.PasswordSalt))
            return false;

        var (hash, salt) = PasswordHasher.Hash(newPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;

        // Cleared here and nowhere else — this is the only route by which a
        // temporary password stops being the account's password.
        user.MustChangePassword = false;

        await db.SaveChangesAsync();
        return true;
    }

    // Note there is deliberately no "set this user's password" method here
    // beyond the two above. Admin creating or renaming a user goes through
    // SaveUserAsync (which always forces a change on next login), and
    // platform support goes through PlatformController, which does its own
    // role check before touching anything. A general-purpose password setter
    // on this class would be reachable from any caller that already has an
    // AuthService, which is most of them.
}
