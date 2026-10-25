using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Data.Messaging;
using SivayaanHMS.Data.Security;

namespace SivayaanHMS.Tests;

public sealed class MovableClock(DateTime start) : IClock
{
    public DateTime Now { get; set; } = start;
}

/// <summary>An OTP gateway whose answers the test decides, and which records
/// what it was asked — so the flow's own rules can be pinned without SivaComm.</summary>
public sealed class ScriptedGateway : IOtpGateway
{
    public string Name => "SivaComm";
    public Queue<OtpVerifyResult> VerifyScript { get; } = new();
    public Queue<OtpStatus> ConsumeScript { get; } = new();
    public List<OtpSendRequest> Sent { get; } = [];
    public List<(string Handle, string Reset, string User)> Consumed { get; } = [];
    public List<string> Completed { get; } = [];
    public bool ThrowOnVerify { get; set; }

    public Task<OtpSendResult> SendAsync(OtpSendRequest request, CancellationToken ct)
    {
        Sent.Add(request);
        return Task.FromResult(new OtpSendResult(OtpStatus.Queued, $"challenge-{Sent.Count}"));
    }

    public Task<OtpSendResult> ResendAsync(string challengeId, OtpSendRequest request, CancellationToken ct) =>
        Task.FromResult(new OtpSendResult(OtpStatus.Queued, challengeId));

    public Task<OtpVerifyResult> VerifyAsync(string challengeId, string resetReference, Guid tenantId, Guid userId, string code, CancellationToken ct)
    {
        if (ThrowOnVerify) throw new TaskCanceledException("simulated timeout");
        return Task.FromResult(VerifyScript.Count > 0 ? VerifyScript.Dequeue() : new OtpVerifyResult(OtpStatus.Verified, GrantHandle: "handle-1"));
    }

    public Task<OtpStatus> ConsumeAsync(string grantHandle, string resetReference, string userReference, CancellationToken ct)
    {
        Consumed.Add((grantHandle, resetReference, userReference));
        return Task.FromResult(ConsumeScript.Count > 0 ? ConsumeScript.Dequeue() : OtpStatus.Consumed);
    }

    public Task ReportCompletedAsync(string challengeId, string resetReference, CancellationToken ct)
    {
        Completed.Add(resetReference);
        return Task.CompletedTask;
    }
}

/// <summary>Captures what would have been sent, so the local flow's code can be read back.</summary>
public sealed class CapturingSender : IMessageSender
{
    public List<OutboundMessage> Messages { get; } = [];

    public Task<bool> SendAsync(string phone, OutboundMessage message, CancellationToken ct = default)
    {
        Messages.Add(message);
        return Task.FromResult(true);
    }
}

/// <summary>
/// The source application's half of the reset: it owns the transaction, the
/// verified-or-not decision as recorded in its own database, and the
/// password. These pin that the new password is accepted only after a
/// backend-verified code, that failures and outages are never successes, and
/// that an unknown account is indistinguishable from a wrong code.
/// </summary>
public sealed class PasswordResetFlowTests : IAsyncLifetime
{
    private readonly TestDb _db = new();
    private readonly MovableClock _clock = new(DateTime.Now);
    private readonly ScriptedGateway _gateway = new();
    private Tenant _tenant = null!;
    private User _user = null!;

    private const string OldPassword = "Old-Pass-1234";
    private const string NewPassword = "New-Pass-5678";

    public async Task InitializeAsync()
    {
        await _db.MigrateAsync();
        _tenant = new Tenant { Slug = "sunrise", ClinicName = "Sunrise Clinic", IsActive = true };
        var (hash, salt) = PasswordHasher.Hash(OldPassword);
        _user = new User
        {
            Username = "drsri@sunrise", DisplayName = "Dr Sri", PasswordHash = hash, PasswordSalt = salt,
            Role = UserRole.Admin, IsActive = true, Phone = "98765 43210", MustChangePassword = true,
        };

        await using var db = _db.CreateContext(_tenant.Id);
        db.Tenants.Add(_tenant);
        db.Users.Add(_user);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private PasswordResetFlow Flow(IOtpGateway? gateway = null) =>
        new(_db.Options, _clock, gateway ?? _gateway, NullLogger<PasswordResetFlow>.Instance);

    private async Task<bool> PasswordIsAsync(string password)
    {
        await using var db = _db.CreateContext(_tenant.Id);
        var u = await db.Users.AsNoTracking().SingleAsync(x => x.Id == _user.Id);
        return PasswordHasher.Verify(password, u.PasswordHash, u.PasswordSalt);
    }

    [Fact]
    public async Task Verified_code_then_new_password_changes_it_and_reports_completion()
    {
        var flow = Flow();
        var token = await flow.StartAsync(_tenant, _user, default);

        var sent = Assert.Single(_gateway.Sent);
        Assert.Equal("98765 43210", sent.Phone);
        Assert.Equal($"u-{_user.Id:N}", sent.UserReference);
        Assert.DoesNotContain("drsri", sent.UserReference);

        Assert.Equal(ResetStepStatus.Verified, (await flow.VerifyAsync(token, "123456", default)).Status);
        Assert.Equal(ResetStepStatus.PasswordChanged, (await flow.CompleteAsync(token, NewPassword, default)).Status);

        Assert.True(await PasswordIsAsync(NewPassword));
        var consumed = Assert.Single(_gateway.Consumed);
        Assert.Equal("handle-1", consumed.Handle);
        Assert.Equal(sent.ResetReference, consumed.Reset);
        Assert.Equal(sent.ResetReference, Assert.Single(_gateway.Completed));

        await using var db = _db.CreateContext(_tenant.Id);
        Assert.False((await db.Users.SingleAsync(u => u.Id == _user.Id)).MustChangePassword);
        var txn = await db.PasswordResetTransactions.SingleAsync();
        Assert.Equal(PasswordResetStage.PasswordChanged, txn.Stage);
        Assert.Null(txn.GrantHandle);
        Assert.NotEqual(token, txn.TokenHash);
    }

    [Fact]
    public async Task The_new_password_is_refused_until_the_backend_has_verified_the_code()
    {
        var flow = Flow();
        var token = await flow.StartAsync(_tenant, _user, default);

        Assert.Equal(ResetStepStatus.NotVerified, (await flow.CompleteAsync(token, NewPassword, default)).Status);

        Assert.True(await PasswordIsAsync(OldPassword));
        Assert.Empty(_gateway.Consumed);
    }

    [Fact]
    public async Task A_wrong_code_reports_remaining_tries_and_keeps_the_password_step_locked()
    {
        _gateway.VerifyScript.Enqueue(new OtpVerifyResult(OtpStatus.InvalidOtp, 4));
        var flow = Flow();
        var token = await flow.StartAsync(_tenant, _user, default);

        var r = await flow.VerifyAsync(token, "000000", default);

        Assert.Equal(ResetStepStatus.InvalidOtp, r.Status);
        Assert.Equal(4, r.RemainingAttempts);
        Assert.Equal(ResetStepStatus.NotVerified, (await flow.CompleteAsync(token, NewPassword, default)).Status);
        Assert.True(await PasswordIsAsync(OldPassword));
    }

    [Theory]
    [InlineData(OtpStatus.Expired, ResetStepStatus.Expired)]
    [InlineData(OtpStatus.AttemptsExceeded, ResetStepStatus.AttemptsExceeded)]
    [InlineData(OtpStatus.AlreadyUsed, ResetStepStatus.AlreadyUsed)]
    [InlineData(OtpStatus.Cancelled, ResetStepStatus.Cancelled)]
    [InlineData(OtpStatus.NotFound, ResetStepStatus.Cancelled)]
    public async Task Terminal_failures_end_the_reset(OtpStatus from, ResetStepStatus expected)
    {
        _gateway.VerifyScript.Enqueue(new OtpVerifyResult(from));
        var flow = Flow();
        var token = await flow.StartAsync(_tenant, _user, default);

        Assert.Equal(expected, (await flow.VerifyAsync(token, "123456", default)).Status);

        // Ended: even a gateway that would now say yes is not asked.
        Assert.Equal(ResetStepStatus.Cancelled, (await flow.VerifyAsync(token, "123456", default)).Status);
        Assert.Equal(ResetStepStatus.NotVerified, (await flow.CompleteAsync(token, NewPassword, default)).Status);
    }

    [Fact]
    public async Task A_temporary_error_at_verify_is_not_success_and_can_be_retried()
    {
        _gateway.VerifyScript.Enqueue(new OtpVerifyResult(OtpStatus.TemporaryError));
        var flow = Flow();
        var token = await flow.StartAsync(_tenant, _user, default);

        Assert.Equal(ResetStepStatus.TemporaryError, (await flow.VerifyAsync(token, "123456", default)).Status);
        Assert.Equal(ResetStepStatus.NotVerified, (await flow.CompleteAsync(token, NewPassword, default)).Status);

        Assert.Equal(ResetStepStatus.Verified, (await flow.VerifyAsync(token, "123456", default)).Status);
    }

    [Fact]
    public async Task A_timeout_reaching_the_otp_service_is_a_temporary_error()
    {
        _gateway.ThrowOnVerify = true;
        var flow = Flow();
        var token = await flow.StartAsync(_tenant, _user, default);

        Assert.Equal(ResetStepStatus.TemporaryError, (await flow.VerifyAsync(token, "123456", default)).Status);
        Assert.Equal(ResetStepStatus.NotVerified, (await flow.CompleteAsync(token, NewPassword, default)).Status);
    }

    [Fact]
    public async Task A_temporary_error_at_consume_changes_nothing_and_a_retry_completes()
    {
        _gateway.ConsumeScript.Enqueue(OtpStatus.TemporaryError);
        var flow = Flow();
        var token = await flow.StartAsync(_tenant, _user, default);
        await flow.VerifyAsync(token, "123456", default);

        Assert.Equal(ResetStepStatus.TemporaryError, (await flow.CompleteAsync(token, NewPassword, default)).Status);
        Assert.True(await PasswordIsAsync(OldPassword));

        Assert.Equal(ResetStepStatus.PasswordChanged, (await flow.CompleteAsync(token, NewPassword, default)).Status);
        Assert.True(await PasswordIsAsync(NewPassword));

        // Both attempts used the same reset reference — SivaComm's idempotency key.
        Assert.Equal(2, _gateway.Consumed.Count);
        Assert.Equal(_gateway.Consumed[0].Reset, _gateway.Consumed[1].Reset);
    }

    [Fact]
    public async Task A_resubmit_after_a_lost_response_succeeds_without_a_second_change()
    {
        var flow = Flow();
        var token = await flow.StartAsync(_tenant, _user, default);
        await flow.VerifyAsync(token, "123456", default);
        await flow.CompleteAsync(token, NewPassword, default);

        var retry = await flow.CompleteAsync(token, NewPassword, default);

        Assert.Equal(ResetStepStatus.PasswordChanged, retry.Status);
        Assert.Single(_gateway.Consumed);

        // Long after, the same token is simply used up.
        _clock.Now = _clock.Now.AddMinutes(10);
        Assert.Equal(ResetStepStatus.AlreadyUsed, (await flow.CompleteAsync(token, NewPassword, default)).Status);
    }

    [Fact]
    public async Task A_grant_the_otp_service_refuses_does_not_change_the_password()
    {
        _gateway.ConsumeScript.Enqueue(OtpStatus.AlreadyUsed);
        var flow = Flow();
        var token = await flow.StartAsync(_tenant, _user, default);
        await flow.VerifyAsync(token, "123456", default);

        Assert.Equal(ResetStepStatus.AlreadyUsed, (await flow.CompleteAsync(token, NewPassword, default)).Status);
        Assert.True(await PasswordIsAsync(OldPassword));
    }

    [Fact]
    public async Task An_expired_reset_cannot_be_completed()
    {
        var flow = Flow();
        var token = await flow.StartAsync(_tenant, _user, default);
        await flow.VerifyAsync(token, "123456", default);

        _clock.Now = _clock.Now.AddMinutes(31);

        Assert.Equal(ResetStepStatus.Expired, (await flow.CompleteAsync(token, NewPassword, default)).Status);
        Assert.Empty(_gateway.Consumed);
    }

    [Fact]
    public async Task Starting_a_new_reset_retires_a_verified_older_one()
    {
        var flow = Flow();
        var first = await flow.StartAsync(_tenant, _user, default);
        await flow.VerifyAsync(first, "123456", default);

        await flow.StartAsync(_tenant, _user, default);

        Assert.Equal(ResetStepStatus.NotVerified, (await flow.CompleteAsync(first, NewPassword, default)).Status);
        Assert.True(await PasswordIsAsync(OldPassword));
    }

    [Fact]
    public async Task A_short_password_is_refused_before_anything_else()
    {
        var flow = Flow();
        var token = await flow.StartAsync(_tenant, _user, default);
        await flow.VerifyAsync(token, "123456", default);

        Assert.Equal(ResetStepStatus.InvalidPassword, (await flow.CompleteAsync(token, "short", default)).Status);
        Assert.Empty(_gateway.Consumed);
    }

    [Fact]
    public async Task An_unknown_account_fails_exactly_like_a_wrong_code()
    {
        var flow = Flow();
        var decoy = await flow.StartAsync(null, null, default);
        Assert.Empty(_gateway.Sent);

        for (var left = 4; left >= 1; left--)
        {
            var r = await flow.VerifyAsync(decoy, "123456", default);
            Assert.Equal(ResetStepStatus.InvalidOtp, r.Status);
            Assert.Equal(left, r.RemainingAttempts);
        }

        var fifth = await flow.VerifyAsync(decoy, "123456", default);
        Assert.Equal(ResetStepStatus.AttemptsExceeded, fifth.Status);
        Assert.Equal(ResetStepStatus.CodeSent, (await flow.ResendAsync(decoy, default)).Status);
    }

    [Fact]
    public async Task A_user_without_a_phone_gets_a_decoy_too()
    {
        await using (var db = _db.CreateContext(_tenant.Id))
        {
            var u = await db.Users.SingleAsync(x => x.Id == _user.Id);
            u.Phone = null;
            await db.SaveChangesAsync();
            _user = u;
        }

        var flow = Flow();
        var token = await flow.StartAsync(_tenant, _user, default);

        Assert.Empty(_gateway.Sent);
        Assert.Equal(ResetStepStatus.InvalidOtp, (await flow.VerifyAsync(token, "123456", default)).Status);
    }

    [Fact]
    public async Task A_made_up_token_gets_nowhere()
    {
        var flow = Flow();
        Assert.Equal(ResetStepStatus.InvalidOtp, (await flow.VerifyAsync("not-a-token", "123456", default)).Status);
        Assert.Equal(ResetStepStatus.NotVerified, (await flow.CompleteAsync("not-a-token", NewPassword, default)).Status);
    }

    [Fact]
    public async Task Local_mode_end_to_end_with_the_code_from_the_message()
    {
        var sender = new CapturingSender();
        var local = new LocalOtpGateway(_db.Options, _clock, sender);
        var flow = Flow(local);

        var token = await flow.StartAsync(_tenant, _user, default);
        var code = Assert.Single(sender.Messages).Parameters[1];

        var wrong = await flow.VerifyAsync(token, code == "000000" ? "111111" : "000000", default);
        Assert.Equal(ResetStepStatus.InvalidOtp, wrong.Status);
        Assert.Equal(4, wrong.RemainingAttempts);

        Assert.Equal(ResetStepStatus.Verified, (await flow.VerifyAsync(token, code, default)).Status);
        Assert.Equal(ResetStepStatus.PasswordChanged, (await flow.CompleteAsync(token, NewPassword, default)).Status);
        Assert.True(await PasswordIsAsync(NewPassword));
    }
}
