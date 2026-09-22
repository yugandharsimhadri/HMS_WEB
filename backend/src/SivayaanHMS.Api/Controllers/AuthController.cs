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

public record ResetPasswordRequest(string Username, string Code, string NewPassword);

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
public record ForgotPasswordResponse(string Message);

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
    IMessageSender messages,
    ILogger<AuthController> logger) : ControllerBase
{
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
    /// </summary>
    [HttpPost("forgot-password")]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(
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

        var text = $"{tenant.ClinicName}: your Sivayaan HMS password reset code is {code}. " +
                   $"It expires in {PasswordResetService.Lifetime.TotalMinutes:0} minutes. " +
                   "If you did not ask for it, ignore this message and tell your clinic admin.";

        try
        {
            await messages.SendAsync(user.Phone!, MessagePurpose.PasswordResetCode, text, ct);
        }
        catch (Exception ex)
        {
            // Logged, never surfaced. A provider outage must not become a
            // different response that tells a stranger the account is real.
            logger.LogError(ex, "Could not send a password-reset code for {UserId}.", user.Id);
        }

        return Ok(new ForgotPasswordResponse(Always));
    }

    /// <summary>
    /// Step two: hand back the code and the new password.
    ///
    /// Unlike step one this one does say what went wrong, because by now the
    /// caller has demonstrated they hold a code — and "wrong code" versus
    /// "expired" versus "ask for a new one" is the difference between a
    /// person retyping six digits and a person giving up. What it still never
    /// says is whether the *account* exists: an unknown username is reported
    /// exactly as a wrong code.
    /// </summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
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
