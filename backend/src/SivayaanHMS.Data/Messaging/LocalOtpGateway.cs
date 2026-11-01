using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data.Messaging;

/// <summary>
/// The OTP gateway HMS_WEB has always had, behind the new interface: codes in
/// its own PasswordResetCodes table, sent through IMessageSender (the log, or
/// WhatsApp directly). The default, so nothing changes for a deployment that
/// has not set up SivaCom.
///
/// Verification is authoritative here — there is no second service to ask —
/// so the "grant" is simply proof, held on the reset transaction, that this
/// process verified the code; consuming it has nothing further to check.
/// </summary>
public sealed class LocalOtpGateway(DbContextOptions<AppDbContext> dbOptions, IClock clock, IMessageSender messages) : IOtpGateway
{
    public const string ProviderName = "Local";
    public string Name => ProviderName;

    public async Task<OtpSendResult> SendAsync(OtpSendRequest request, CancellationToken ct)
    {
        var resets = new PasswordResetService(new TenantScopedDbContextFactory(dbOptions, request.TenantId), clock);
        var code = await resets.IssueAsync(request.UserId, request.TenantId, ct);
        if (code is null) return new(OtpStatus.RateLimited);

        var minutes = $"{PasswordResetService.Lifetime.TotalMinutes:0}";
        var message = new OutboundMessage(
            MessagePurpose.PasswordResetCode,
            $"{request.ClinicName}: your Sivayaan HMS password reset code is {code}. " +
            $"It expires in {minutes} minutes. " +
            "If you did not ask for it, ignore this message and tell your clinic admin.",
            // {{1}} clinic, {{2}} code, {{3}} minutes — the order the approved
            // WhatsApp template declares. See docs/WHATSAPP_SETUP.md.
            new[] { request.ClinicName, code, minutes });

        // Whether the transport took it is deliberately not reported upward:
        // the answer to the browser is the same either way.
        await messages.SendAsync(request.Phone, message, ct);
        return new(OtpStatus.Queued, ProviderName);
    }

    public Task<OtpSendResult> ResendAsync(string challengeId, OtpSendRequest request, CancellationToken ct) =>
        SendAsync(request, ct);

    public async Task<OtpVerifyResult> VerifyAsync(
        string challengeId, string resetReference, Guid tenantId, Guid userId, string code, CancellationToken ct)
    {
        var resets = new PasswordResetService(new TenantScopedDbContextFactory(dbOptions, tenantId), clock);
        var (outcome, remaining) = await resets.VerifyCodeAsync(userId, code, ct);

        return outcome switch
        {
            ResetOutcome.Success => new(OtpStatus.Verified, GrantHandle: Convert.ToHexString(RandomNumberGenerator.GetBytes(16))),
            ResetOutcome.Expired => new(OtpStatus.Expired),
            ResetOutcome.TooManyAttempts => new(OtpStatus.AttemptsExceeded, 0),

            // No live code at all: spent, superseded by a newer one, or never sent.
            ResetOutcome.InvalidCode when remaining == 0 => new(OtpStatus.Expired),
            _ => new(OtpStatus.InvalidOtp, remaining),
        };
    }

    public Task<OtpStatus> ConsumeAsync(string grantHandle, string resetReference, string userReference, CancellationToken ct) =>
        Task.FromResult(OtpStatus.Consumed);

    public Task ReportCompletedAsync(string challengeId, string resetReference, CancellationToken ct) => Task.CompletedTask;
}
