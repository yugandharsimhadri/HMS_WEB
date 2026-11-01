using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SivayaanHMS.Core;
using SivayaanHMS.Data.Messaging;
using SivayaanHMS.Data.Security;

namespace SivayaanHMS.Data;

public enum ResetStepStatus
{
    CodeSent,
    Verified,
    PasswordChanged,
    InvalidOtp,
    Expired,
    AttemptsExceeded,
    AlreadyUsed,
    Cancelled,
    RateLimited,

    /// <summary>The new-password step was attempted without a verified code.</summary>
    NotVerified,

    InvalidPassword,
    TemporaryError,
}

public sealed record ResetStepResult(ResetStepStatus Status, int? RemainingAttempts = null);

/// <summary>
/// "I forgot my password" in three steps — ask for a code, verify it, then
/// set a new password — with this application owning every decision that
/// matters: who the user is, which phone the code goes to, whether the code
/// was verified, and the password itself.
///
/// The OTP itself is delegated to an <see cref="IOtpGateway"/> (local table,
/// or SivaCom). Whatever it says, two rules hold:
///
///   * the new password is accepted only when this application's own reset
///     transaction says the code was verified by the backend — the browser's
///     word is never enough; and
///   * a timeout or outage anywhere is a retryable failure, never a success.
///
/// The browser holds one opaque token per reset. An unknown username gets a
/// token too, backed by a decoy row that fails exactly as a real one does,
/// so no step reveals whether an account exists.
/// </summary>
public sealed class PasswordResetFlow(
    DbContextOptions<AppDbContext> dbOptions,
    IClock clock,
    IOtpGateway gateway,
    ILogger<PasswordResetFlow> logger)
{
    /// <summary>The whole reset — code, then new password — must finish within this.</summary>
    public static readonly TimeSpan TransactionLifetime = TimeSpan.FromMinutes(30);

    /// <summary>How long a repeated "set password" for an already-finished reset
    /// is answered with success — the lost-response retry window.</summary>
    private static readonly TimeSpan CompletedReplayWindow = TimeSpan.FromMinutes(5);

    // ── Step 1 ───────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a reset and returns the browser's token. For an account that
    /// can be reset, a code is requested from the gateway; for anything else
    /// (unknown, inactive, no phone) a decoy transaction is recorded instead.
    /// The caller answers the browser identically either way.
    /// </summary>
    public async Task<string> StartAsync(Tenant? tenant, User? user, CancellationToken ct)
    {
        var token = NewToken();
        var now = clock.Now;
        var real = tenant is not null && user is not null && user.IsActive && !string.IsNullOrWhiteSpace(user.Phone);

        await using var db = new TenantScopedDbContextFactory(dbOptions, real ? tenant!.Id : Guid.Empty).CreateDbContext();

        if (real)
        {
            // An earlier reset still in flight ends now: one live reset per user.
            await db.PasswordResetTransactions.IgnoreQueryFilters()
                .Where(t => t.UserId == user!.Id && (t.Stage == PasswordResetStage.CodeSent || t.Stage == PasswordResetStage.Verified))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Stage, PasswordResetStage.Closed).SetProperty(t => t.GrantHandle, (string?)null), ct);
        }

        var txn = new PasswordResetTransaction
        {
            UserId = real ? user!.Id : Guid.Empty,
            TokenHash = Hash(token),
            Provider = gateway.Name,
            Stage = PasswordResetStage.CodeSent,
            ExpiresOn = now + TransactionLifetime,
        };
        db.PasswordResetTransactions.Add(txn);
        await db.SaveChangesAsync(ct);

        if (!real) return token;

        try
        {
            var sent = await gateway.SendAsync(Request(txn, tenant!, user!), ct);
            if (sent.Status == OtpStatus.Queued && sent.ChallengeId is not null)
            {
                txn.ChallengeId = sent.ChallengeId;
                await db.SaveChangesAsync(ct);
            }
            else
            {
                // Rate-limited or unavailable: the row stays a decoy, so the
                // browser cannot tell this apart from a sent code.
                logger.LogWarning("Reset code for user {UserId} was not sent: {Status}.", user!.Id, sent.Status);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not request a reset code for user {UserId}.", user!.Id);
        }

        return token;
    }

    /// <summary>
    /// A new code for the same reset. Always answers CodeSent: a cooldown
    /// or a send cap reported to the browser would differ from a decoy's
    /// answer and so reveal the account. The page enforces its own cooldown.
    /// </summary>
    public async Task<ResetStepResult> ResendAsync(string token, CancellationToken ct)
    {
        await using var db = Lookup();
        var txn = await FindAsync(db, token, ct);

        if (txn is null || txn.Stage != PasswordResetStage.CodeSent || txn.ExpiresOn <= clock.Now ||
            txn.ChallengeId.Length == 0 || txn.Provider != gateway.Name)
            return new(ResetStepStatus.CodeSent);

        var (tenant, user) = await AccountAsync(db, txn, ct);
        if (tenant is null || user is null || string.IsNullOrWhiteSpace(user.Phone)) return new(ResetStepStatus.CodeSent);

        try
        {
            var r = await gateway.ResendAsync(txn.ChallengeId, Request(txn, tenant, user), ct);
            if (r.Status != OtpStatus.Queued) logger.LogInformation("Resend for user {UserId}: {Status}.", user.Id, r.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not resend a reset code for user {UserId}.", user.Id);
        }

        return new(ResetStepStatus.CodeSent);
    }

    // ── Step 2 ───────────────────────────────────────────────────────────

    public async Task<ResetStepResult> VerifyAsync(string token, string code, CancellationToken ct)
    {
        var now = clock.Now;
        await using var db = Lookup();
        var txn = await FindAsync(db, token, ct);

        if (txn is null) return new(ResetStepStatus.InvalidOtp);

        switch (txn.Stage)
        {
            case PasswordResetStage.Verified when txn.ExpiresOn > now:
                return new(ResetStepStatus.Verified);
            case PasswordResetStage.PasswordChanged:
                return new(ResetStepStatus.AlreadyUsed);
            case PasswordResetStage.Closed:
                return new(ResetStepStatus.Cancelled);
        }

        if (txn.ExpiresOn <= now || txn.Provider != gateway.Name)
        {
            await CloseAsync(db, txn, ct);
            return new(ResetStepStatus.Expired);
        }

        code = (code ?? "").Trim();

        // A decoy: fail like a real code does, five times, then lock.
        if (txn.ChallengeId.Length == 0)
        {
            txn.DecoyAttempts++;
            var left = PasswordResetService.MaxAttempts - txn.DecoyAttempts;
            if (left <= 0) txn.Stage = PasswordResetStage.Closed;
            await db.SaveChangesAsync(ct);
            return left <= 0 ? new(ResetStepStatus.AttemptsExceeded, 0) : new(ResetStepStatus.InvalidOtp, left);
        }

        OtpVerifyResult result;
        try
        {
            result = await gateway.VerifyAsync(txn.ChallengeId, Reference(txn), txn.TenantId, txn.UserId, code, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Verifying a reset code failed for transaction {TransactionId}.", txn.Id);
            return new(ResetStepStatus.TemporaryError);
        }

        switch (result.Status)
        {
            case OtpStatus.Verified when !string.IsNullOrEmpty(result.GrantHandle):
                txn.Stage = PasswordResetStage.Verified;
                txn.VerifiedOn = now;
                txn.GrantHandle = result.GrantHandle;
                await db.SaveChangesAsync(ct);
                return new(ResetStepStatus.Verified);

            case OtpStatus.InvalidOtp:
                return new(ResetStepStatus.InvalidOtp, result.RemainingAttempts);

            case OtpStatus.RateLimited:
                return new(ResetStepStatus.RateLimited);

            case OtpStatus.Expired:
                await CloseAsync(db, txn, ct);
                return new(ResetStepStatus.Expired);

            case OtpStatus.AttemptsExceeded:
                await CloseAsync(db, txn, ct);
                return new(ResetStepStatus.AttemptsExceeded, 0);

            case OtpStatus.AlreadyUsed:
                await CloseAsync(db, txn, ct);
                return new(ResetStepStatus.AlreadyUsed);

            case OtpStatus.Cancelled or OtpStatus.NotFound:
                await CloseAsync(db, txn, ct);
                return new(ResetStepStatus.Cancelled);

            default:
                return new(ResetStepStatus.TemporaryError);
        }
    }

    // ── Step 3 ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the new password — only for a transaction this application has
    /// recorded as verified, and only after the OTP service has consumed the
    /// grant. The password never leaves this method except as a hash into
    /// this application's own Users table.
    /// </summary>
    public async Task<ResetStepResult> CompleteAsync(string token, string newPassword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return new(ResetStepStatus.InvalidPassword);

        var now = clock.Now;
        await using var db = Lookup();
        var txn = await FindAsync(db, token, ct);

        if (txn is null) return new(ResetStepStatus.NotVerified);

        // A retry after a lost response: the change already happened.
        if (txn.Stage == PasswordResetStage.PasswordChanged)
            return txn.CompletedOn > now - CompletedReplayWindow ? new(ResetStepStatus.PasswordChanged) : new(ResetStepStatus.AlreadyUsed);

        if (txn.Stage != PasswordResetStage.Verified || txn.GrantHandle is null) return new(ResetStepStatus.NotVerified);

        if (txn.ExpiresOn <= now || txn.Provider != gateway.Name)
        {
            await CloseAsync(db, txn, ct);
            return new(ResetStepStatus.Expired);
        }

        var user = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == txn.UserId && u.TenantId == txn.TenantId && !u.IsDeleted, ct);
        if (user is null || !user.IsActive)
        {
            await CloseAsync(db, txn, ct);
            return new(ResetStepStatus.NotVerified);
        }

        OtpStatus consumed;
        try
        {
            consumed = await gateway.ConsumeAsync(txn.GrantHandle, Reference(txn), UserReference(txn.UserId), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Consuming the reset grant failed for transaction {TransactionId}.", txn.Id);
            return new(ResetStepStatus.TemporaryError);
        }

        if (consumed != OtpStatus.Consumed)
        {
            if (consumed == OtpStatus.TemporaryError) return new(ResetStepStatus.TemporaryError);

            await CloseAsync(db, txn, ct);
            return new(consumed switch
            {
                OtpStatus.Expired => ResetStepStatus.Expired,
                OtpStatus.AlreadyUsed => ResetStepStatus.AlreadyUsed,
                _ => ResetStepStatus.NotVerified,
            });
        }

        var (hash, salt) = PasswordHasher.Hash(newPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;

        // Same as the original reset: a password they just chose themselves
        // is not one to force them to change, and it supersedes any
        // support-issued temporary password.
        user.MustChangePassword = false;

        txn.Stage = PasswordResetStage.PasswordChanged;
        txn.CompletedOn = now;
        txn.GrantHandle = null;

        try
        {
            // One SaveChanges: the password and the transaction's end are one
            // database transaction. RowVersion on the reset row means two
            // racing submissions cannot both pass — the second fails here.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await using var fresh = Lookup();
            var again = await fresh.PasswordResetTransactions.IgnoreQueryFilters().AsNoTracking().FirstAsync(t => t.Id == txn.Id, ct);
            return again.Stage == PasswordResetStage.PasswordChanged ? new(ResetStepStatus.PasswordChanged) : new(ResetStepStatus.TemporaryError);
        }

        logger.LogInformation("Password reset completed for user {UserId} (transaction {TransactionId}).", user.Id, txn.Id);

        try
        {
            await gateway.ReportCompletedAsync(txn.ChallengeId, Reference(txn), ct);
        }
        catch (Exception ex)
        {
            // Reporting only. The password is changed; that is the answer.
            logger.LogWarning(ex, "Could not report reset completion for transaction {TransactionId}.", txn.Id);
        }

        return new(ResetStepStatus.PasswordChanged);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>This application's id for the reset, as the OTP service knows it.</summary>
    public static string Reference(PasswordResetTransaction txn) => $"hms-{txn.Id:N}";

    /// <summary>Pseudonymous: the user's internal id, never the username or phone.</summary>
    public static string UserReference(Guid userId) => $"u-{userId:N}";

    private static OtpSendRequest Request(PasswordResetTransaction txn, Tenant tenant, User user) =>
        new(Reference(txn), UserReference(user.Id), user.Phone!, tenant.Id, user.Id, tenant.ClinicName);

    private AppDbContext Lookup() => new TenantScopedDbContextFactory(dbOptions, Guid.Empty).CreateDbContext();

    private static Task<PasswordResetTransaction?> FindAsync(AppDbContext db, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return Task.FromResult<PasswordResetTransaction?>(null);
        var hash = Hash(token);
        return db.PasswordResetTransactions.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TokenHash == hash && !t.IsDeleted, ct);
    }

    private static async Task<(Tenant?, User?)> AccountAsync(AppDbContext db, PasswordResetTransaction txn, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == txn.TenantId && t.IsActive, ct);
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == txn.UserId && u.IsActive && !u.IsDeleted, ct);
        return (tenant, user);
    }

    private static async Task CloseAsync(AppDbContext db, PasswordResetTransaction txn, CancellationToken ct)
    {
        txn.Stage = PasswordResetStage.Closed;
        txn.GrantHandle = null;
        await db.SaveChangesAsync(ct);
    }

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
