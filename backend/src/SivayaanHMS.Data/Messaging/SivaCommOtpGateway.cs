using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SivayaanHMS.Data.Messaging;

/// <summary>
/// OTPs through SivaComm. SivaComm generates, sends and verifies the code;
/// this application still owns the user, the phone on file, the reset
/// transaction and — only ever here — the password.
///
/// The verification grant SivaComm returns is kept on the reset transaction
/// encrypted (AES-GCM, with a key derived from our SivaComm secret), so a
/// read of this application's database alone does not yield a usable grant.
/// </summary>
public sealed class SivaCommOtpGateway(SivaCommClient client, IOptions<SivaCommOptions> options, ILogger<SivaCommOtpGateway> logger) : IOtpGateway
{
    public const string ProviderName = "SivaComm";
    public string Name => ProviderName;

    private readonly byte[] _grantKey = HKDF.DeriveKey(
        HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(options.Value.Secret), 32, info: "hms/grant-handle/v1"u8.ToArray());

    public async Task<OtpSendResult> SendAsync(OtpSendRequest request, CancellationToken ct)
    {
        var r = await client.PostAsync("/api/v1/otp/challenges", new
        {
            resetReference = request.ResetReference,
            userReference = request.UserReference,
            phone = request.Phone,
            purpose = "PASSWORD_RESET",
        }, ct);

        return new(Map(r.Status), r.Str("challengeId"));
    }

    public async Task<OtpSendResult> ResendAsync(string challengeId, OtpSendRequest request, CancellationToken ct)
    {
        var r = await client.PostAsync($"/api/v1/otp/challenges/{Uri.EscapeDataString(challengeId)}/resend",
            new { resetReference = request.ResetReference }, ct);
        return new(Map(r.Status), challengeId);
    }

    public async Task<OtpVerifyResult> VerifyAsync(
        string challengeId, string resetReference, Guid tenantId, Guid userId, string code, CancellationToken ct)
    {
        var r = await client.PostAsync($"/api/v1/otp/challenges/{Uri.EscapeDataString(challengeId)}/verify",
            new { resetReference, otp = code }, ct);

        var status = Map(r.Status);
        if (status != OtpStatus.Verified) return new(status, r.Int("remainingAttempts"));

        // Belt and braces: a VERIFIED without a grant, or for another
        // transaction, is not something to act on.
        var grant = r.Str("verificationGrant");
        if (string.IsNullOrEmpty(grant) || r.Str("resetReference") != resetReference)
        {
            logger.LogError("SivaComm reported VERIFIED without a usable grant for {ResetReference}.", resetReference);
            return new(OtpStatus.TemporaryError);
        }

        return new(OtpStatus.Verified, GrantHandle: Protect(grant));
    }

    public async Task<OtpStatus> ConsumeAsync(string grantHandle, string resetReference, string userReference, CancellationToken ct)
    {
        string grant;
        try { grant = Unprotect(grantHandle); }
        catch (CryptographicException)
        {
            // The secret was rotated mid-reset. The person starts again.
            logger.LogWarning("A stored grant for {ResetReference} could not be decrypted (key rotated?).", resetReference);
            return OtpStatus.Expired;
        }

        // The reset reference doubles as the idempotency key: a retry of the
        // same reset after a lost response is "consumed" again, anything else
        // is a replay.
        var r = await client.PostAsync("/api/v1/grants/consume", new { grant, resetReference }, ct, idempotencyKey: resetReference);
        var status = Map(r.Status);

        if (status == OtpStatus.Consumed && (r.Str("resetReference") != resetReference || r.Str("userReference") != userReference))
        {
            logger.LogError("SivaComm consumed a grant bound to a different reset or user than {ResetReference}.", resetReference);
            return OtpStatus.NotFound;
        }

        return status;
    }

    public async Task ReportCompletedAsync(string challengeId, string resetReference, CancellationToken ct)
    {
        var r = await client.PostAsync($"/api/v1/otp/challenges/{Uri.EscapeDataString(challengeId)}/completion",
            new { resetReference, outcome = "PASSWORD_CHANGED" }, ct);
        if (r.Status != "COMPLETED")
            logger.LogWarning("SivaComm did not record completion of {ResetReference}: {Status}.", resetReference, r.Status);
    }

    private static OtpStatus Map(string status) => status switch
    {
        "QUEUED" => OtpStatus.Queued,
        "VERIFIED" => OtpStatus.Verified,
        "CONSUMED" => OtpStatus.Consumed,
        "INVALID_OTP" => OtpStatus.InvalidOtp,
        "EXPIRED" => OtpStatus.Expired,
        "ATTEMPTS_EXCEEDED" => OtpStatus.AttemptsExceeded,
        "ALREADY_USED" => OtpStatus.AlreadyUsed,
        "CANCELLED" => OtpStatus.Cancelled,
        "RATE_LIMITED" => OtpStatus.RateLimited,
        "CHALLENGE_NOT_FOUND" or "GRANT_NOT_FOUND" => OtpStatus.NotFound,

        // INVALID_REQUEST, CONFLICT, UNAUTHORIZED and anything new: not a
        // success, and not the person's fault.
        _ => OtpStatus.TemporaryError,
    };

    private string Protect(string grant)
    {
        var plain = Encoding.UTF8.GetBytes(grant);
        var blob = new byte[12 + 16 + plain.Length];
        RandomNumberGenerator.Fill(blob.AsSpan(0, 12));
        using var aes = new AesGcm(_grantKey, 16);
        aes.Encrypt(blob.AsSpan(0, 12), plain, blob.AsSpan(28), blob.AsSpan(12, 16));
        return Convert.ToBase64String(blob);
    }

    private string Unprotect(string handle)
    {
        byte[] blob;
        try { blob = Convert.FromBase64String(handle); }
        catch (FormatException) { throw new CryptographicException("Not a grant handle."); }
        if (blob.Length < 28) throw new CryptographicException("Not a grant handle.");

        var plain = new byte[blob.Length - 28];
        using var aes = new AesGcm(_grantKey, 16);
        aes.Decrypt(blob.AsSpan(0, 12), blob.AsSpan(28), blob.AsSpan(12, 16), plain);
        return Encoding.UTF8.GetString(plain);
    }
}

/// <summary>
/// Welcome messages through SivaComm, so they appear in its dashboard with
/// everything else. Reset codes never come through here in SivaComm mode —
/// those go through <see cref="SivaCommOtpGateway"/>.
/// </summary>
public sealed class SivaCommMessageSender(SivaCommClient client, ILogger<SivaCommMessageSender> logger) : IMessageSender
{
    public async Task<bool> SendAsync(string phone, OutboundMessage message, CancellationToken ct = default)
    {
        if (message.Purpose != MessagePurpose.Welcome)
        {
            logger.LogError("SivaCommMessageSender was asked to send a {Purpose}; only Welcome goes this way.", message.Purpose);
            return false;
        }

        // Derived from the content, so a retried registration does not send
        // the same welcome twice.
        var reference = "welcome-" + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(phone + "\n" + string.Join("\n", message.Parameters))))[..32];

        var r = await client.PostAsync("/api/v1/messages/welcome",
            new { clientReference = reference, phone, parameters = message.Parameters }, ct);

        if (r.Status != "QUEUED")
        {
            logger.LogWarning("SivaComm did not queue a welcome message: {Status}.", r.Status);
            return false;
        }
        return true;
    }
}
