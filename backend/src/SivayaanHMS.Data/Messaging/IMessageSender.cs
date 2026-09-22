using Microsoft.Extensions.Logging;

namespace SivayaanHMS.Data.Messaging;

/// <summary>What a message is for. The transport may care — WhatsApp
/// requires a pre-approved template per purpose, and Indian SMS requires a
/// DLT-registered template per purpose — so the purpose travels with the
/// message rather than being guessed from its text.</summary>
public enum MessagePurpose
{
    Welcome,
    PasswordResetCode,
}

/// <summary>
/// Sends a message to a phone. One method, because everything above it only
/// ever wants to say one thing to one number.
///
/// Deliberately an interface with a logging implementation rather than a
/// direct call to a provider: the provider is not chosen yet, and the
/// password-reset flow has to be testable long before a paid account and a
/// template approval exist. Swapping in MSG91, Twilio or WhatsApp Cloud API
/// later is one class and one line in Program.cs, and nothing that calls
/// this changes.
/// </summary>
public interface IMessageSender
{
    /// <param name="phone">As the user typed it. No country is assumed.</param>
    /// <returns>
    /// Whether it was handed to the transport — not whether it arrived.
    /// Nothing upstream may branch on this in a way the caller can observe:
    /// telling an anonymous caller that a message failed tells them the
    /// account exists.
    /// </returns>
    Task<bool> SendAsync(string phone, MessagePurpose purpose, string message, CancellationToken ct = default);
}

/// <summary>
/// The development sender: writes the message to the application log instead
/// of sending it.
///
/// This is what makes "forgot password" usable on a laptop with no provider
/// account — the code appears in the API's own console, so the whole flow
/// can be walked through end to end. It is also why <see cref="Program"/>
/// must not leave this wired up in Production: a real clinic would generate
/// reset codes that nobody receives, and every code would be sitting in the
/// log for anyone who can read it.
/// </summary>
public sealed class LoggingMessageSender(ILogger<LoggingMessageSender> logger) : IMessageSender
{
    public Task<bool> SendAsync(string phone, MessagePurpose purpose, string message, CancellationToken ct = default)
    {
        logger.LogWarning(
            "No SMS/WhatsApp provider is configured, so this message was NOT sent.\n" +
            "  To      : {Phone}\n  Purpose : {Purpose}\n  Message : {Message}",
            phone, purpose, message);

        return Task.FromResult(true);
    }
}
