using Microsoft.Extensions.Options;
using SivayaanHMS.Data.Security;

namespace SivayaanHMS.Api.Auth;

/// <summary>
/// The support identity's credentials.
///
/// Config-driven rather than compiled in. The desktop could hardcode this
/// because the binary sat on one clinic's PC and the "platform" was that
/// machine; on a server, one constant in source is a single key to every
/// clinic on the estate, and source is the one place a secret is guaranteed
/// to be copied, cloned and pushed. The default below is a development
/// convenience only — see the guard in Program.cs.
/// </summary>
public class PlatformAdminOptions
{
    public const string SectionName = "PlatformAdmin";

    public string Username { get; set; } = "EnterpriseAdmin";

    /// <summary>Override via environment variable
    /// (<c>PlatformAdmin__Password</c>) or a secret store before this runs
    /// anywhere real.</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Checks the one identity that exists outside every clinic.
///
/// EnterpriseAdmin is never a row in the Users table — it is a constant
/// checked here — so it cannot be listed, renamed, edited or deleted from
/// any screen inside a clinic, and no clinic's own Admin can see that it
/// exists. It carries no tenant, which is the whole point: the token it
/// receives has no tenant claim, so AppDbContext's global filter resolves
/// to Guid.Empty and every clinic-scoped endpoint reads back nothing even
/// if one is somehow reached. Its reach is exactly the two things the
/// platform console exposes — resetting a clinic admin's password, and
/// moving a licence date — and no patient record anywhere.
/// </summary>
public class PlatformAdminService(IOptions<PlatformAdminOptions> options, ILogger<PlatformAdminService> logger)
{
    private readonly PlatformAdminOptions _options = options.Value;

    /// <summary>Hashed once at first use so the comparison below runs
    /// through the same constant-time path as every clinic password, rather
    /// than a plain string equality that leaks its answer in its timing.</summary>
    private readonly Lazy<(string Hash, string Salt)> _credential =
        new(() => PasswordHasher.Hash(options.Value.Password));

    public string Username => _options.Username;

    public bool IsPlatformAdminUsername(string? username)
        => string.Equals(username?.Trim(), _options.Username, StringComparison.OrdinalIgnoreCase);

    public bool Verify(string? password)
    {
        // An unset password must never authenticate. Without this, a
        // deployment that forgot to configure one would hash "" and let
        // anyone in by leaving the box blank.
        if (string.IsNullOrEmpty(_options.Password))
        {
            logger.LogError("A platform-admin sign-in was attempted but no PlatformAdmin:Password is configured.");
            return false;
        }

        var (hash, salt) = _credential.Value;
        return PasswordHasher.Verify(password ?? string.Empty, hash, salt);
    }
}
