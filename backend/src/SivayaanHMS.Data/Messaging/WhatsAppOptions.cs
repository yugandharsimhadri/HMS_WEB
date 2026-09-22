namespace SivayaanHMS.Data.Messaging;

/// <summary>
/// Everything needed to send through Meta's WhatsApp Cloud API, from the
/// "WhatsApp" section of appsettings. See docs/WHATSAPP_SETUP.md.
///
/// Configured or not configured is decided by <see cref="IsConfigured"/>
/// alone, so a half-filled section cannot start the application in a state
/// where codes are generated and silently never sent.
/// </summary>
public class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    /// <summary>Permanent access token for the system user, not the 24-hour
    /// one the dashboard shows first. A temporary token works and then stops
    /// working a day later, which is a confusing way to find this out.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>The number's id from the WhatsApp Manager — a long number,
    /// not the phone number itself.</summary>
    public string PhoneNumberId { get; set; } = string.Empty;

    public string ApiVersion { get; set; } = "v21.0";

    /// <summary>
    /// Names of the approved templates. A business cannot send free-form
    /// WhatsApp text to somebody who has not messaged it in the last 24
    /// hours — everything here is business-initiated, so both are templates
    /// and both must be approved by Meta before anything sends.
    /// </summary>
    public string WelcomeTemplate { get; set; } = "hms_welcome";
    public string ResetCodeTemplate { get; set; } = "hms_password_reset";

    /// <summary>Must match the language the template was approved in, exactly
    /// — "en" and "en_US" are different templates as far as Meta is
    /// concerned, and a mismatch fails at send time.</summary>
    public string LanguageCode { get; set; } = "en";

    /// <summary>
    /// Prepended when somebody typed a bare national number. "98765 43210"
    /// is what an Indian clinic types and WhatsApp needs "919876543210".
    ///
    /// Empty means never guess: a number without a country code is then
    /// rejected rather than sent to whichever country shares those digits.
    /// </summary>
    public string DefaultCountryCode { get; set; } = "";

    /// <summary>Both halves or neither. A token without a number id, or the
    /// reverse, is a misconfiguration and not a reason to start sending.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(PhoneNumberId);
}
