using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SivayaanHMS.Api.Auth;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

public record LoginRequest(string Username, string Password);
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
public class AuthController(DbContextOptions<AppDbContext> dbOptions, IClock clock, JwtTokenService tokens) : ControllerBase
{
    // Deliberately one message for every way this can fail. Telling an
    // anonymous caller "no such clinic" or "no such user" turns the login
    // page into a free directory of every clinic on the platform and every
    // account in it.
    private const string InvalidMessage = "Incorrect username or password.";

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        if (!UserName.TrySplit(request.Username, out _, out var clinicSlug))
            return Unauthorized(InvalidMessage);

        // Guid.Empty: the Tenants table is deliberately outside the tenant
        // filter, so any pinned context can read it — that is what makes
        // resolving a clinic before anyone is authenticated possible at all.
        await using var lookup = new TenantScopedDbContextFactory(dbOptions, Guid.Empty).CreateDbContext();
        var tenant = await lookup.Tenants.FirstOrDefaultAsync(t => t.Slug == clinicSlug);

        if (tenant is null || !tenant.IsActive) return Unauthorized(InvalidMessage);

        var factory = new TenantScopedDbContextFactory(dbOptions, tenant.Id);
        var authService = new AuthService(factory, clock, NullLogger<AuthService>.Instance);

        var result = await authService.LoginAsync(request.Username, request.Password);

        if (result.Outcome == LoginOutcome.Failed) return Unauthorized(InvalidMessage);

        if (result.Outcome == LoginOutcome.EnterpriseRecovery)
            return StatusCode(StatusCodes.Status501NotImplemented,
                "Password recovery isn't wired up on the web edition yet.");

        var user = result.User!;
        var token = tokens.IssueToken(tenant.Id, user);

        return Ok(new LoginResponse(
            token, user.Username, user.Role.ToString(), user.MustChangePassword, tenant.ClinicName));
    }
}
