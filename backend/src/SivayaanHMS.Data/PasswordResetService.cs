using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;
using SivayaanHMS.Data.Security;

namespace SivayaanHMS.Data;

public enum ResetOutcome
{
    Success,
    /// <summary>No live code, or the one given does not match, or it is spent.</summary>
    InvalidCode,
    Expired,
    /// <summary>Too many wrong guesses; the code is dead and a new one is needed.</summary>
    TooManyAttempts,
}

/// <summary>
/// "I forgot my password", done with a code sent to the phone on the
/// account.
///
/// The rules here exist because the alternative — letting anyone who knows a
/// username set a new password — is exactly what this feature would
/// otherwise be. Each one is a limit on how much an attacker gets per
/// attempt:
///
///   * the code is six digits and short-lived, so a stolen phone screen from
///     last week is worth nothing;
///   * only a hash of it is stored, so the table is useless to a reader;
///   * it dies after a few wrong guesses, because a million possibilities is
///     minutes of scripted work otherwise;
///   * issuing a new one kills the old, so codes cannot be stockpiled;
///   * and a cap on how many can be sent in a window stops the feature being
///     used to spam somebody's phone at the clinic's expense.
/// </summary>
public class PasswordResetService(IDbContextFactory<AppDbContext> factory, IClock clock)
{
    public const int CodeLength = 6;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int MaxAttempts = 5;

    /// <summary>At most this many codes per user per window, so the button
    /// cannot be used to bombard a number.</summary>
    public const int MaxSendsPerWindow = 3;
    public static readonly TimeSpan SendWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Creates a code for the user and returns it in the clear — the only
    /// moment it exists in readable form, for handing straight to the
    /// message sender. Returns null when the user has already asked too many
    /// times recently; the caller must still answer as though it worked.
    /// </summary>
    public async Task<string?> IssueAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var now = clock.Now;
        await using var db = await factory.CreateDbContextAsync(ct);

        var recent = await db.PasswordResetCodes
            .IgnoreQueryFilters()
            .CountAsync(c => c.UserId == userId && c.CreatedAt >= now - SendWindow, ct);

        if (recent >= MaxSendsPerWindow) return null;

        // Any earlier code stops working the moment a new one is asked for.
        // Two live codes would double an attacker's chances for free, and a
        // person who asked twice is reading the newest message anyway.
        var live = await db.PasswordResetCodes
            .IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.UsedOn == null && c.ExpiresOn > now)
            .ToListAsync(ct);

        foreach (var old in live) old.UsedOn = now;

        // RandomNumberGenerator, not Random: this is a credential, and
        // Random is predictable from its seed.
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString($"D{CodeLength}");
        var (hash, salt) = PasswordHasher.Hash(code);

        db.PasswordResetCodes.Add(new PasswordResetCode
        {
            TenantId = tenantId,
            UserId = userId,
            CodeHash = hash,
            CodeSalt = salt,
            ExpiresOn = now + Lifetime,
            CreatedAt = now,
        });

        await db.SaveChangesAsync(ct);
        return code;
    }

    /// <summary>
    /// Spends a code and sets the new password. Everything about the outcome
    /// is returned to the caller, which decides how much of it a stranger is
    /// allowed to be told.
    /// </summary>
    public async Task<ResetOutcome> RedeemAsync(Guid userId, string code, string newPassword, CancellationToken ct = default)
    {
        var now = clock.Now;
        await using var db = await factory.CreateDbContextAsync(ct);

        var (outcome, candidate) = await CheckAsync(db, userId, code, now, ct);
        if (outcome != ResetOutcome.Success) return outcome;

        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return ResetOutcome.InvalidCode;

        var (hash, salt) = PasswordHasher.Hash(newPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;

        // They just chose this password themselves, so there is nothing to
        // force them to change at the next sign-in. Clearing it also matters
        // for an account that was mid-reset from support: the code they just
        // proved they hold is the same evidence that flag was waiting for.
        user.MustChangePassword = false;
        user.UpdatedAt = now;

        candidate!.UsedOn = now;
        await db.SaveChangesAsync(ct);

        return ResetOutcome.Success;
    }

    /// <summary>
    /// Checks and spends a code <i>without</i> touching the password — the
    /// verify step of the three-step flow, where the new password is asked
    /// for only after this says yes. Same rules as <see cref="RedeemAsync"/>.
    /// </summary>
    public async Task<(ResetOutcome Outcome, int RemainingAttempts)> VerifyCodeAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var now = clock.Now;
        await using var db = await factory.CreateDbContextAsync(ct);

        var (outcome, candidate) = await CheckAsync(db, userId, code, now, ct);
        if (outcome == ResetOutcome.Success)
        {
            candidate!.UsedOn = now;
            await db.SaveChangesAsync(ct);
        }

        return (outcome, candidate is null ? 0 : Math.Max(0, MaxAttempts - candidate.Attempts));
    }

    /// <summary>The live code for the user and whether <paramref name="code"/>
    /// matches it; counts and saves a wrong guess.</summary>
    private static async Task<(ResetOutcome, PasswordResetCode?)> CheckAsync(
        AppDbContext db, Guid userId, string code, DateTime now, CancellationToken ct)
    {
        var candidate = await db.PasswordResetCodes
            .IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.UsedOn == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (candidate is null) return (ResetOutcome.InvalidCode, null);
        if (candidate.Attempts >= MaxAttempts) return (ResetOutcome.TooManyAttempts, candidate);

        if (candidate.ExpiresOn <= now)
        {
            candidate.UsedOn = now;
            await db.SaveChangesAsync(ct);
            return (ResetOutcome.Expired, candidate);
        }

        if (!PasswordHasher.Verify(code, candidate.CodeHash, candidate.CodeSalt))
        {
            candidate.Attempts++;

            // Burn it on the last failure rather than leaving a dead row that
            // still says "wrong code" — the next try should say plainly that
            // a new code is needed.
            if (candidate.Attempts >= MaxAttempts) candidate.UsedOn = now;

            await db.SaveChangesAsync(ct);
            return (candidate.Attempts >= MaxAttempts ? ResetOutcome.TooManyAttempts : ResetOutcome.InvalidCode, candidate);
        }

        return (ResetOutcome.Success, candidate);
    }
}
