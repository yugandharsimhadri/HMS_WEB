namespace SivayaanHMS.Api.Auth;

/// <summary>Bound from the "Jwt" configuration section. The signing key
/// ships with an obviously-fake development default in appsettings.json —
/// production must override it via environment variable or secret store;
/// Program.cs refuses to start otherwise. See its own check.</summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "SivayaanHMS";
    public string Audience { get; set; } = "SivayaanHMS";
    public int ExpiryMinutes { get; set; } = 480;
}
