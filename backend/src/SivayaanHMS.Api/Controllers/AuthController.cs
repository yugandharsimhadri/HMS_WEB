using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SivayaanHMS.Api.Auth;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

public record LoginRequest(string ClinicSlug, string Username, string Password);
public record LoginResponse(string Token, string Username, string Role, bool MustChangePassword);

/// <summary>
/// Sign-in. The clinic slug resolves which tenant this login is for —
/// before that is known, nothing about "Admin" or a password means
/// anything, since a username is only unique within one clinic (see
/// AppDbContext's (TenantId, Username) index). Everything after that
/// resolution is exactly AuthService.LoginAsync, unchanged from the
/// desktop's own logic.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(DbContextOptions<AppDbContext> dbOptions, IClock clock, JwtTokenService tokens) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        await using var lookup = new TenantScopedDbContextFactory(dbOptions, Guid.Empty).CreateDbContext();
        var tenant = await lookup.Tenants.FirstOrDefaultAsync(t => t.Slug == request.ClinicSlug.Trim().ToLowerInvariant());

        // Deliberately the same message as a wrong password below — a
        // clinic slug is effectively part of the login, and confirming
        // "that clinic doesn't exist" to an anonymous caller is exactly
        // the kind of detail an enumeration attack wants handed to it.
        const string invalidMessage = "Incorrect clinic, username or password.";

        if (tenant is null || !tenant.IsActive) return Unauthorized(invalidMessage);

        var factory = new TenantScopedDbContextFactory(dbOptions, tenant.Id);
        var authService = new AuthService(factory, clock, NullLogger<AuthService>.Instance);

        var result = await authService.LoginAsync(request.Username, request.Password);

        if (result.Outcome == LoginOutcome.Failed) return Unauthorized(invalidMessage);

        if (result.Outcome == LoginOutcome.EnterpriseRecovery)
            return StatusCode(StatusCodes.Status501NotImplemented,
                "Password recovery isn't wired up on the web edition yet.");

        var user = result.User!;
        var token = tokens.IssueToken(tenant.Id, user);

        return Ok(new LoginResponse(token, user.Username, user.Role.ToString(), user.MustChangePassword));
    }
}
