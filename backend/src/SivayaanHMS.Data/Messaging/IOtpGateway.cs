namespace SivayaanHMS.Data.Messaging;

/// <summary>
/// Every answer an OTP service can give this application, normalised. The
/// names match SivaComm's wire statuses so a log line reads the same on
/// both sides.
/// </summary>
public enum OtpStatus
{
    Queued,
    Verified,
    Consumed,
    InvalidOtp,
    Expired,
    AttemptsExceeded,
    AlreadyUsed,
    Cancelled,
    RateLimited,
    NotFound,

    /// <summary>Timeout, network failure, 5xx, unreadable response, or SivaComm
    /// refusing our signature. Never success — the caller retries or fails.</summary>
    TemporaryError,
}

/// <param name="ResetReference">This application's own transaction id.</param>
/// <param name="UserReference">A pseudonymous id for the user — never the username.</param>
/// <param name="Phone">The number already on the account. Never one the browser supplied.</param>
public sealed record OtpSendRequest(
    string ResetReference, string UserReference, string Phone, Guid TenantId, Guid UserId, string ClinicName);

public sealed record OtpSendResult(OtpStatus Status, string? ChallengeId = null);

/// <param name="GrantHandle">Set only on Verified: the verification grant in
/// a form only this gateway can turn back into the grant.</param>
public sealed record OtpVerifyResult(OtpStatus Status, int? RemainingAttempts = null, string? GrantHandle = null);

/// <summary>
/// Where reset codes come from and are checked. Two implementations:
/// <see cref="LocalOtpGateway"/> (this application's own PasswordResetCodes
/// table, as before) and <see cref="SivaCommOtpGateway"/> (the central
/// service). Chosen once at startup by SivaComm:Enabled.
///
/// Nothing here sets a password. That stays in PasswordResetFlow, in this
/// application, against this application's own database.
/// </summary>
public interface IOtpGateway
{
    /// <summary>Stored on each reset transaction, so a mode switch
    /// mid-reset cannot send a verify to the wrong service.</summary>
    string Name { get; }

    Task<OtpSendResult> SendAsync(OtpSendRequest request, CancellationToken ct);

    Task<OtpSendResult> ResendAsync(string challengeId, OtpSendRequest request, CancellationToken ct);

    Task<OtpVerifyResult> VerifyAsync(string challengeId, string resetReference, Guid tenantId, Guid userId, string code, CancellationToken ct);

    /// <summary>Spends the grant. Safe to repeat with the same reset
    /// reference after a lost response: the answer is Consumed again.</summary>
    Task<OtpStatus> ConsumeAsync(string grantHandle, string resetReference, string userReference, CancellationToken ct);

    /// <summary>Reporting only; failures are logged and ignored.</summary>
    Task ReportCompletedAsync(string challengeId, string resetReference, CancellationToken ct);
}
