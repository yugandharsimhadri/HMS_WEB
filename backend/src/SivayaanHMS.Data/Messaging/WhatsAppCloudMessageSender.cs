using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SivayaanHMS.Data.Messaging;

/// <summary>
/// Sends through Meta's WhatsApp Cloud API.
///
/// Everything this application sends is business-initiated — a welcome after
/// signup, a code somebody asked for — and WhatsApp only allows free-form
/// text inside a 24-hour window that the *customer* opens by messaging the
/// business first. So every message here is a **template** message, approved
/// by Meta in advance, with the variable parts sent as parameters. That is
/// why OutboundMessage carries its pieces as well as its sentence, and why a
/// new kind of message means a new approved template rather than new text.
/// </summary>
public sealed class WhatsAppCloudMessageSender(
    HttpClient http,
    IOptions<WhatsAppOptions> options,
    ILogger<WhatsAppCloudMessageSender> logger) : IMessageSender
{
    private readonly WhatsAppOptions _options = options.Value;

    public async Task<bool> SendAsync(string phone, OutboundMessage message, CancellationToken ct = default)
    {
        if (!TryNormalise(phone, out var to))
        {
            // Logged, not thrown: one staff member's malformed number must
            // not break the flow for everybody else, and the caller is
            // forbidden from behaving differently anyway.
            logger.LogWarning(
                "Not sending a {Purpose} message: '{Phone}' is not a number WhatsApp can use. " +
                "It needs a country code, typed in or set as WhatsApp:DefaultCountryCode.",
                message.Purpose, phone);
            return false;
        }

        var template = message.Purpose == MessagePurpose.Welcome
            ? _options.WelcomeTemplate
            : _options.ResetCodeTemplate;

        var payload = new
        {
            messaging_product = "whatsapp",
            to,
            type = "template",
            template = new
            {
                name = template,
                language = new { code = _options.LanguageCode },
                components = new object[]
                {
                    new
                    {
                        type = "body",
                        parameters = message.Parameters
                            .Select(p => new { type = "text", text = p })
                            .ToArray(),
                    },
                },
            },
        };

        var url = $"https://graph.facebook.com/{_options.ApiVersion}/{_options.PhoneNumberId}/messages";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.Authorization = new("Bearer", _options.AccessToken);

            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // Meta's errors are specific and worth keeping verbatim. The
                // three that actually happen: an expired token (the
                // dashboard's first token lasts 24 hours), a template name or
                // language that does not match an approved one, and a number
                // that has never used WhatsApp.
                logger.LogError(
                    "WhatsApp refused a {Purpose} message ({Status}). Response: {Body}",
                    message.Purpose, (int)response.StatusCode, Truncate(body));
                return false;
            }

            // The message id is worth keeping: it is what a delivery webhook
            // reports against later, and the only way to answer "was it
            // delivered?" rather than "did we hand it over?".
            logger.LogInformation(
                "WhatsApp accepted a {Purpose} message. Response: {Body}",
                message.Purpose, Truncate(body));
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not reach WhatsApp to send a {Purpose} message.", message.Purpose);
            return false;
        }
    }

    /// <summary>
    /// Digits only, with a country code: WhatsApp wants "919876543210" — no
    /// plus, no spaces.
    ///
    /// A bare national number gets a country code only when one is
    /// configured. Otherwise it is refused, because guessing would send a
    /// clinic's reset codes to whoever in another country happens to hold
    /// those digits.
    /// </summary>
    /// <remarks>Public so it can be tested directly: this is the step that
    /// decides which handset a reset code reaches, and it is worth pinning
    /// separately from any call to Meta.</remarks>
    public bool TryNormalise(string? phone, out string normalised)
    {
        normalised = new string((phone ?? "").Where(char.IsDigit).ToArray());

        // 00 is the other way of writing +, and some clinics type it.
        if (normalised.StartsWith("00")) normalised = normalised[2..];

        var cc = new string(_options.DefaultCountryCode.Where(char.IsDigit).ToArray());

        // Ten digits or fewer is a national number; anything longer already
        // carries its country code.
        if (normalised.Length <= 10 && cc.Length > 0 && !normalised.StartsWith(cc))
            normalised = cc + normalised;

        return normalised.Length is >= 10 and <= 15;
    }

    private static string Truncate(string s) => s.Length <= 400 ? s : s[..400] + "…";
}
