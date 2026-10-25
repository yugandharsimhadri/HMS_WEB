namespace SivayaanHMS.Data;

/// <summary>
/// Whether clinic users can reset a forgotten password themselves, by a
/// one-time code to the phone on their account.
///
/// Off, the Forgot-password link is hidden and every reset endpoint refuses,
/// which puts users back where they were before OTP reset existed: their
/// clinic admin resets them, or platform support resets the admin. On, the
/// code comes from wherever Messaging:Mode says — this application's own
/// table (Local) or the central service (SivaComm).
///
/// Welcome messages are not affected; they follow Messaging:Mode either way.
/// </summary>
public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>True by default, so a deployment that never sets it keeps the
    /// self-service reset it already has.</summary>
    public bool Enabled { get; set; } = true;
}
