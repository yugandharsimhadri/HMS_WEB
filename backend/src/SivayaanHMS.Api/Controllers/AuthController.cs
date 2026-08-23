using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Api.Auth;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

public record LoginRequest(string Username, string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

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
    PlatformAdminService platformAdmin) : ControllerBase
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
}
