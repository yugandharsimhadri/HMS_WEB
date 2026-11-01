using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Api.Auth;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Data.Messaging;

namespace SivayaanHMS.Api.Controllers;

public record LoginRequest(string Username, string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ForgotPasswordRequest(string Username);

/// <summary>
/// Either the three-step shape — <paramref name="ResetToken"/> from
/// forgot-password, after verify-reset-code said VERIFIED — or the original
/// one-step shape (username + code), which is kept for a browser still
/// running the previous build and only works in Local mode.
/// </summary>
public record ResetPasswordRequest(string? Username, string? Code, string NewPassword, string? ResetToken = null);

public record VerifyResetCodeRequest(string ResetToken, string Code);

public record ResendResetCodeRequest(string ResetToken);

/// <summary>Every outcome of the verify and set-password steps, in one shape.
/// <see cref="Status"/> is what the screen branches on; <see cref="Message"/> is
/// what it shows.</summary>
public record ResetStepResponse(string Status, string Message, int? RemainingAttempts = null);

/// <param name="PasswordResetEnabled">PasswordReset:Enabled. When false the
/// sign-in page hides "Forgot password" and the reset endpoints answer 403.</param>
/// <param name="SivaComEnabled">SivaCom:Enabled. False (the default): the
/// original forgot-password page and endpoints, exactly as before SivaCom.
/// True: the three-step page backed by SivaCom OTPs.</param>
public record AuthOptionsResponse(bool PasswordResetEnabled, bool SivaComEnabled);

/// <summary>
/// What the "send me a code" screen is told: one sentence, byte-for-byte the
/// same whether or not the account exists.
///
/// It carried a masked hint ("••••••••3210") until testing showed that was
/// the leak the rest of the endpoint was written to avoid — the hint is
/// present for a real account and absent for an invented one, so the field
/// alone answers "does this username exist?" for anyone who asks twice. The
/// convenience of naming which phone to look at is not worth handing out a
/// list of every account on the platform.
/// </summary>
/// <param name="ResetToken">SivaCom flow only. Opaque, and issued for every
/// request — a real reset or a decoy — so its presence says nothing either.
/// Omitted entirely otherwise, so the original flow's response is unchanged:
/// <c>{ "message": "…" }</c>.</param>
public record ForgotPasswordResponse(
    string Message,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? ResetToken = null);

/// <summary><see cref="Role"/> tells the browser which application it just
/// signed into: the support console for "EnterpriseAdmin", the clinic shell
/// for everything else. It is a routing hint only — the API re-derives this
/// from the token's own claims on every request and trusts nothing the
/// client remembers.</summary>
public record LoginResponse(string Token, string Username, string Role, bool MustChangePassword, string ClinicName);

/// <summary>
/// Sign-in: a username and a password, nothing else.
///
/// The clinic is carried by the username itself — "reception@twinkle", see
/// <see cref="UserName"/> — so it is asked for once at registration and
/// never again. Resolving it here rather than from a third box on the login
/// page is what lets a receptionist type exactly what they were given.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(
    DbContextOptions<AppDbContext> dbOptions,
    IClock clock,
    JwtTokenService tokens,
    PlatformAdminService platformAdmin,
    PasswordResetFlow resetFlow,
    IMessageSender messages,
    Microsoft.Extensions.Options.IOptions<PasswordResetOptions> resetOptions,
    Microsoft.Extensions.Options.IOptions<SivaComOptions> sivaComOptions,
    ILogger<AuthController> logger) : ControllerBase
{
    private const string ResetDisabled =
        "Self-service password reset is turned off. Ask your clinic admin to reset your password.";

    private bool ResetEnabled => resetOptions.Value.Enabled;

    /// <summary>
    /// SivaCom:Enabled — off by default. Off, every reset endpoint behaves
    /// exactly as it did before SivaCom existed: the original two-step page,
    /// the original code table, no reset-transaction rows. The SivaCom
    /// three-step flow runs only when this is true.
    /// </summary>
    private bool SivaComEnabled => sivaComOptions.Value.Enabled;

    /// <summary>
    /// What the sign-in screens need to know before anyone signs in: whether to
    /// offer "Forgot password", and which reset page to show. Anonymous, and
    /// the same for every caller: it describes this deployment, not any account.
    /// </summary>
    [HttpGet("options")]
    public ActionResult<AuthOptionsResponse> Options() => Ok(new AuthOptionsResponse(ResetEnabled, SivaComEnabled));

    // Deliberately one message for every way this can fail. Telling an
    // anonymous caller "no such clinic" or "no such user" turns the login
    // page into a free directory of every clinic on the platform and every
    // account in it.
    private const string InvalidMessage = "Incorrect username or password.";

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        // Checked first, and before any tenant is resolved: this identity
        // belongs to no clinic and carries no "@clinic" suffix to resolve
        // one from. It never reaches AuthService or a tenant-scoped context.
        if (platformAdmin.IsPlatformAdminUsername(request.Username))
        {
            if (!platformAdmin.Verify(request.Password)) return Unauthorized(InvalidMessage);

            return Ok(new LoginResponse(
                tokens.IssuePlatformToken(platformAdmin.Username),
                platformAdmin.Username,
                TenantClaimTypes.PlatformAdminRole,
                MustChangePassword: false,
                ClinicName: "Platform support"));
        }

        if (!UserName.TrySplit(request.Username, out _, out var clinicSlug))
            return Unauthorized(InvalidMessage);

        // Guid.Empty: the Tenants table is deliberately outside the tenant
        // filter, so any pinned context can read it — that is what makes
        // resolving a clinic before anyone is authenticated possible at all.
        await using var lookup = new TenantScopedDbContextFactory(dbOptions, Guid.Empty).CreateDbContext();
        var tenant = await lookup.Tenants.FirstOrDefaultAsync(t => t.Slug == clinicSlug);

        if (tenant is null || !tenant.IsActive) return Unauthorized(InvalidMessage);

        var factory = new TenantScopedDbContextFactory(dbOptions, tenant.Id);
        var authService = new AuthService(factory, clock);

        var result = await authService.LoginAsync(request.Username, request.Password);

        if (result.Outcome == LoginOutcome.Failed) return Unauthorized(InvalidMessage);

        // Licence is checked *after* the password, never before. The message
        // names the clinic and its expiry date, which is only safe to show
        // someone who has just proved they work there — shown to an
        // anonymous caller it would confirm which clinics exist and when
        // each one's subscription lapsed.
        if (LicenseRules.IsExpired(tenant.LicenseExpiresOn, clock.Now))
        {
            var expiry = tenant.LicenseExpiresOn!.Value;
            return StatusCode(StatusCodes.Status402PaymentRequired,
                $"{tenant.ClinicName}'s licence expired on {expiry:d MMMM yyyy}. " +
                "Your records are safe and untouched — contact Sivayaan support to renew.");
        }

        var user = result.User!;
        var token = tokens.IssueToken(tenant.Id, user);

        return Ok(new LoginResponse(
            token, user.Username, user.Role.ToString(), user.MustChangePassword, tenant.ClinicName));
    }

    /// <summary>
    /// The signed-in user choosing their own password.
    ///
    /// This is the other half of a support reset: without it, the temporary
    /// password a support agent read down the phone would quietly become the
    /// account's permanent one, and MustChangePassword would be a flag
    /// nothing ever acted on.
    ///
    /// [Authorize] resolves to the clinic policy (see Program.cs), so a
    /// platform-support token cannot reach this — there is no clinic user
    /// behind it whose password there would be to change.
    /// </summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request, AuthService authService, ICurrentUserContext currentUser)
    {
        if (currentUser.UserId is not { } userId) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            return BadRequest("Choose a password of at least 8 characters.");

        // Rejected specifically, because the likeliest way to arrive here is
        // holding a temporary password a support agent knows — and "change"
        // it to itself would leave that agent's knowledge live for good.
        if (request.NewPassword == request.CurrentPassword)
            return BadRequest("Choose a password different from your current one.");

        if (!await authService.ChangeOwnPasswordAsync(userId, request.CurrentPassword, request.NewPassword))
            return BadRequest("Your current password is not right.");

        return NoContent();
    }

    /// <summary>
    /// Step one of "I forgot my password": send a one-time code to the phone
    /// already on the account.
    ///
    /// **This endpoint always answers the same way.** Unknown clinic, unknown
    /// user, deactivated user, no phone on file, too many codes already sent
    /// — every one of them returns the same 200 and the same sentence. The
    /// temptation to be helpful here ("no such user") is exactly what turns a
    /// login page into a directory of every account on the platform, and the
    /// person who genuinely owns the account learns nothing from the
    /// difference anyway: either the message arrives or it does not.
    ///
    /// Nothing about the phone number comes back at all — not even masked.
    /// See ForgotPasswordResponse for why that changed.
    ///
    /// With SivaCom:Enabled off (the default) this is the original endpoint,
    /// unchanged — see <see cref="ClassicForgotPasswordAsync"/>. With it on,
    /// a reset token comes back too, for every request alike: an unknown
    /// username gets one backed by a decoy that fails the next step the same
    /// way a real reset with a wrong code does.
    /// </summary>
    [HttpPost("forgot-password")]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(
        ForgotPasswordRequest request, CancellationToken ct)
    {
        const string Always = "If that account exists and has a phone number on file, " +
                              "a code is on its way. It is valid for 10 minutes.";

        // Refused before the username is even looked at, so the answer is
        // the same for every caller and says nothing about any account.
        if (!ResetEnabled) return StatusCode(StatusCodes.Status403Forbidden, ResetDisabled);

        if (!SivaComEnabled) return await ClassicForgotPasswordAsync(request, ct);

        var (tenant, user) = await ResolveAsync(request.Username, ct);

        string token;
        try
        {
            token = await resetFlow.StartAsync(tenant, user, ct);
        }
        catch (Exception ex)
        {
            // Our own database, not the provider: a genuine outage, and one a
            // stranger would see for any username alike.
            logger.LogError(ex, "Could not start a password reset.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Password reset is unavailable right now. Try again shortly.");
        }

        return Ok(new ForgotPasswordResponse(Always, token));
    }

    /// <summary>
    /// The forgot-password endpoint exactly as it was before SivaCom:
    /// a code in this application's own PasswordResetCodes table, sent through
    /// IMessageSender (WhatsApp, or the log), and one sentence back. Kept
    /// verbatim so that with SivaCom:Enabled off nothing about this path
    /// changes — not the response, not the tables written.
    /// </summary>
    private async Task<ActionResult<ForgotPasswordResponse>> ClassicForgotPasswordAsync(
        ForgotPasswordRequest request, CancellationToken ct)
    {
        const string Always = "If that account exists and has a phone number on file, " +
                              "a code is on its way. It is valid for 10 minutes.";

        var (tenant, user) = await ResolveAsync(request.Username, ct);

        if (tenant is null || user is null)
            return Ok(new ForgotPasswordResponse(Always));

        if (!user.IsActive || string.IsNullOrWhiteSpace(user.Phone))
            return Ok(new ForgotPasswordResponse(Always));

        var factory = new TenantScopedDbContextFactory(dbOptions, tenant.Id);
        var resets = new PasswordResetService(factory, clock);

        var code = await resets.IssueAsync(user.Id, tenant.Id, ct);

        // Null means the send cap was hit. Same answer: a caller who could
        // tell the difference could use it to probe which accounts are busy.
        if (code is null) return Ok(new ForgotPasswordResponse(Always));

        var minutes = $"{PasswordResetService.Lifetime.TotalMinutes:0}";

        var text = new OutboundMessage(
            MessagePurpose.PasswordResetCode,
            $"{tenant.ClinicName}: your Sivayaan HMS password reset code is {code}. " +
            $"It expires in {minutes} minutes. " +
            "If you did not ask for it, ignore this message and tell your clinic admin.",
            // {{1}} clinic, {{2}} code, {{3}} minutes — the order the approved
            // WhatsApp template declares. See docs/WHATSAPP_SETUP.md.
            new[] { tenant.ClinicName, code, minutes });

        try
        {
            await messages.SendAsync(user.Phone!, text, ct);
        }
        catch (Exception ex)
        {
            // Logged, never surfaced. A provider outage must not become a
            // different response that tells a stranger the account is real.
            logger.LogError(ex, "Could not send a password-reset code for {UserId}.", user.Id);
        }

        return Ok(new ForgotPasswordResponse(Always));
    }

    /// <summary>A fresh code for the same reset. Always the same answer — see
    /// PasswordResetFlow.ResendAsync for why. SivaCom flow only.</summary>
    [HttpPost("resend-reset-code")]
    public async Task<ActionResult<ForgotPasswordResponse>> ResendResetCode(ResendResetCodeRequest request, CancellationToken ct)
    {
        if (!SivaComEnabled) return NotFound();
        if (!ResetEnabled) return StatusCode(StatusCodes.Status403Forbidden, ResetDisabled);

        await resetFlow.ResendAsync(request.ResetToken ?? "", ct);
        return Ok(new ForgotPasswordResponse(
            "If that account exists and has a phone number on file, a new code is on its way."));
    }

    /// <summary>
    /// Step two: check the code — and only the code. The new-password fields
    /// appear on the page after this says VERIFIED, and the next step is
    /// refused unless this application's own record says so; the browser
    /// cannot skip here.
    ///
    /// It does say what went wrong — wrong code, expired, too many attempts —
    /// because that is the difference between retyping six digits and asking
    /// again. A decoy reset answers in exactly the same words and counts.
    /// SivaCom flow only; with SivaCom:Enabled off this endpoint does not exist.
    /// </summary>
    [HttpPost("verify-reset-code")]
    public async Task<IActionResult> VerifyResetCode(VerifyResetCodeRequest request, CancellationToken ct)
    {
        if (!SivaComEnabled) return NotFound();

        // A reset started before the flag was turned off stops here too.
        if (!ResetEnabled) return Disabled();

        var result = await resetFlow.VerifyAsync(request.ResetToken ?? "", request.Code ?? "", ct);
        return Step(result);
    }

    /// <summary>
    /// Two shapes, one per flow:
    ///
    ///   * SivaCom:Enabled off (default) — the original: username + code +
    ///     new password in one request, checked against this application's
    ///     PasswordResetCodes table. Unchanged from before SivaCom.
    ///   * SivaCom:Enabled on — reset token + new password, accepted only for
    ///     a reset whose code SivaCom has verified.
    ///
    /// A request in the other flow's shape is told the page is out of date
    /// (a browser that loaded the page before the flag was switched).
    /// </summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        if (!ResetEnabled) return Disabled();

        const string OutOfDate = "This page is out of date. Reload it and request a new code.";

        if (SivaComEnabled)
        {
            return string.IsNullOrEmpty(request.ResetToken)
                ? BadRequest(OutOfDate)
                : Step(await resetFlow.CompleteAsync(request.ResetToken, request.NewPassword ?? "", ct));
        }

        if (!string.IsNullOrEmpty(request.ResetToken)) return BadRequest(OutOfDate);

        const string BadCode = "That code is not right, or it has expired. Ask for a new one.";

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            return BadRequest("Choose a password of at least 8 characters.");

        var (tenant, user) = await ResolveAsync(request.Username, ct);

        if (tenant is null || user is null || !user.IsActive)
            return BadRequest(BadCode);

        var factory = new TenantScopedDbContextFactory(dbOptions, tenant.Id);
        var resets = new PasswordResetService(factory, clock);

        var outcome = await resets.RedeemAsync(user.Id, request.Code?.Trim() ?? "", request.NewPassword, ct);

        return outcome switch
        {
            ResetOutcome.Success => NoContent(),
            ResetOutcome.Expired => BadRequest("That code has expired. Ask for a new one."),
            ResetOutcome.TooManyAttempts => BadRequest(
                "That code has been tried too many times and is no longer usable. Ask for a new one."),
            _ => BadRequest(BadCode),
        };
    }

    /// <summary>
    /// A reset step's outcome as HTTP. 200 for success; 400 for something the
    /// person can act on; 429 for slow down; 503 for "not you — try again",
    /// which is what every timeout or outage behind this becomes. None of
    /// them is ever a success the screen did not earn.
    /// </summary>
    private ObjectResult Step(ResetStepResult r)
    {
        var (code, message) = r.Status switch
        {
            ResetStepStatus.Verified => (200, "Code verified. Choose a new password."),
            ResetStepStatus.PasswordChanged => (200, "Your password has been changed. Sign in with it now."),
            ResetStepStatus.InvalidOtp => (400, "That code is not right. Check the message and try again."),
            ResetStepStatus.Expired => (400, "That code has expired. Ask for a new one."),
            ResetStepStatus.AttemptsExceeded => (400, "That code has been tried too many times. Start again to get a new one."),
            ResetStepStatus.AlreadyUsed => (400, "This reset has already been used. Start again if you still need to."),
            ResetStepStatus.Cancelled => (400, "This reset is no longer active. Start again to get a new code."),
            ResetStepStatus.NotVerified => (400, "Verify the code from your phone first."),
            ResetStepStatus.InvalidPassword => (400, "Choose a password of at least 8 characters."),
            ResetStepStatus.RateLimited => (429, "Too many tries. Wait a minute and try again."),
            _ => (503, "We could not check that right now. Your code may still be valid — try again in a moment."),
        };

        return StatusCode(code, new ResetStepResponse(StatusName(r.Status), message, r.RemainingAttempts));
    }

    private ObjectResult Disabled() =>
        StatusCode(StatusCodes.Status403Forbidden, new ResetStepResponse("DISABLED", ResetDisabled));

    private static string StatusName(ResetStepStatus s) => s switch
    {
        ResetStepStatus.CodeSent => "CODE_SENT",
        ResetStepStatus.Verified => "VERIFIED",
        ResetStepStatus.PasswordChanged => "PASSWORD_CHANGED",
        ResetStepStatus.InvalidOtp => "INVALID_OTP",
        ResetStepStatus.Expired => "EXPIRED",
        ResetStepStatus.AttemptsExceeded => "ATTEMPTS_EXCEEDED",
        ResetStepStatus.AlreadyUsed => "ALREADY_USED",
        ResetStepStatus.Cancelled => "CANCELLED",
        ResetStepStatus.RateLimited => "RATE_LIMITED",
        ResetStepStatus.NotVerified => "NOT_VERIFIED",
        ResetStepStatus.InvalidPassword => "INVALID_PASSWORD",
        _ => "TEMPORARY_ERROR",
    };

    /// <summary>
    /// Username to clinic and user, without a token and without trusting
    /// anything about either. Returns nulls rather than throwing, so both
    /// endpoints above can treat "no such thing" as ordinary.
    /// </summary>
    private async Task<(Tenant? Tenant, User? User)> ResolveAsync(string? username, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username)) return (null, null);

        // Platform support has no clinic and no phone; it is recovered by
        // whoever holds the server's configuration, not by SMS.
        if (platformAdmin.IsPlatformAdminUsername(username)) return (null, null);

        if (!UserName.TrySplit(username, out _, out var slug)) return (null, null);

        await using var lookup = new TenantScopedDbContextFactory(dbOptions, Guid.Empty).CreateDbContext();
        var tenant = await lookup.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (tenant is null || !tenant.IsActive) return (null, null);

        var user = await lookup.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        return (tenant, user);
    }

}
