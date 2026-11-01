namespace SivayaanHMS.Data;

/// <summary>
/// An optional kill switch for self-service reset, separate from
/// SivaCom:Enabled (which only chooses WHO sends the code).
///
/// Off, the Forgot-password link is hidden and every reset endpoint refuses,
/// which puts users back where they were before OTP reset existed: their
/// clinic admin resets them, or platform support resets the admin. On (the
/// default), the code comes from SivaCom when SivaCom:Enabled, otherwise
/// from this application's own table.
///
/// Welcome messages are not affected; they follow SivaCom:Enabled.
/// </summary>
public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>True by default, so a deployment that never sets it keeps the
    /// self-service reset it already has.</summary>
    public bool Enabled { get; set; } = true;
}
