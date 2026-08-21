using System.Security.Claims;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Auth;

/// <summary>
/// Reads the tenant claim off the current request's validated JWT. This is
/// the one piece that makes AppDbContext's global query filter — and
/// TenantId stamping on new rows — resolve to the right clinic per request,
/// without any controller or service having to pass a tenant id around
/// explicitly. An unauthenticated request (no token, or a token with no
/// tenant claim) resolves to Guid.Empty, same as NullCurrentTenantContext —
/// it reads back zero rows rather than leaking across tenants.
/// </summary>
public class HttpCurrentTenantContext(IHttpContextAccessor accessor) : ICurrentTenantContext
{
    public Guid TenantId
    {
        get
        {
            var claim = accessor.HttpContext?.User.FindFirst(TenantClaimTypes.TenantId)?.Value;
            return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
        }
    }
}

/// <summary>The signed-in user's id, for AppDbContext.Stamp() — the API
/// equivalent of the desktop's CurrentUserService.</summary>
public class HttpCurrentUserContext(IHttpContextAccessor accessor) : ICurrentUserContext
{
    public Guid? UserId
    {
        get
        {
            var claim = accessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(claim, out var id) ? id : null;
        }
    }
}
