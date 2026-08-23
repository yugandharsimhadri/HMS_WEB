using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SivayaanHMS.Core;

namespace SivayaanHMS.Api.Auth;

/// <summary>
/// Issues the token every authenticated request carries. The tenant claim
/// is what makes AppDbContext's global query filter resolve to the right
/// clinic — see HttpCurrentTenantContext — so it is set here, once, at
/// login, rather than trusted from anything the client sends per-request.
/// </summary>
public class JwtTokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public string IssueToken(Guid tenantId, User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(TenantClaimTypes.TenantId, tenantId.ToString())
        };

        return Write(claims, _options.ExpiryMinutes);
    }

    /// <summary>
    /// The support token. Note what is absent: no tenant claim, so
    /// HttpCurrentTenantContext resolves Guid.Empty and every query
    /// AppDbContext filters reads back nothing. That is belt as well as
    /// braces — ClinicPolicy already turns these tokens away at the door —
    /// but it means a route that ever forgets to name its policy still
    /// cannot show one clinic's records to support staff.
    ///
    /// Deliberately shorter-lived than a clinic session. This token can
    /// reset any clinic admin's password on the platform, so it should last
    /// a support call, not a working day.
    /// </summary>
    public string IssuePlatformToken(string username)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, TenantClaimTypes.PlatformAdminRole)
        };

        return Write(claims, PlatformTokenMinutes);
    }

    private const int PlatformTokenMinutes = 60;

    private string Write(Claim[] claims, int expiryMinutes)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
