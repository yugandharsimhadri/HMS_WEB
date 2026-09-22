using Microsoft.Extensions.Logging;

namespace SivayaanHMS.Data.Messaging;

/// <summary>What a message is for. The transport cares: WhatsApp requires a
/// pre-approved template per purpose, and Indian SMS requires a
/// DLT-registered template per purpose — so the purpose travels with the
/// message rather than being guessed from its text.</summary>
public enum MessagePurpose
{
    Welcome,
    PasswordResetCode,
}

/// <summary>
/// One message to one phone, in both the forms a transport might want it.
///
/// <paramref name="Text"/> is the whole sentence, for anything that sends
/// free-form text (an SMS provider, or the log).
///
/// <paramref name="Parameters"/> is the same content in pieces, in the order
/// an approved template declares its {{1}}, {{2}} placeholders. WhatsApp
/// cannot send free-form text to somebody who has not messaged the business
/// in the last 24 hours, and every message here is business-initiated, so on
/// that transport the pieces are what actually gets sent and the sentence is
/// only for the log.
///
/// Both are built by the caller, deliberately. The first version of this had
/// the WhatsApp sender pull the values back out of the finished sentence by
/// looking for ":" and a run of digits — which works until somebody rewords
/// the message and the reset code silently becomes the clinic's phone
/// number.
/// </summary>
public record OutboundMessage(MessagePurpose Purpose, string Text, IReadOnlyList<string> Parameters);

/// <summary>
/// Sends a message to a phone. One method, because everything above it only
/// ever wants to say one thing to one number.
///
/// An interface with a logging implementation, so the password-reset flow is
/// testable long before a paid account and a template approval exist.
/// </summary>
public interface IMessageSender
{
    /// <param name="phone">As the user typed it. The transport normalises.</param>
    /// <returns>
    /// Whether it was handed to the transport — not whether it arrived.
    /// Nothing upstream may branch on this in a way an anonymous caller can
    /// observe: telling them a message failed tells them the account exists.
    /// </returns>
    Task<bool> SendAsync(string phone, OutboundMessage message, CancellationToken ct = default);
}

/// <summary>
/// The development sender: writes the message to the application log instead
/// of sending it.
///
/// This is what makes "forgot password" usable on a laptop with no provider
/// account — the code appears in the API's own console, so the whole flow
/// can be walked through end to end. It is also why Program.cs warns at
/// startup if this is still in use outside Development: a real clinic would
/// generate reset codes nobody receives, each one sitting in the log for
/// whoever can read it.
/// </summary>
public sealed class LoggingMessageSender(ILogger<LoggingMessageSender> logger) : IMessageSender
{
    public Task<bool> SendAsync(string phone, OutboundMessage message, CancellationToken ct = default)
    {
        logger.LogWarning(
            "No SMS/WhatsApp provider is configured, so this message was NOT sent.\n" +
            "  To      : {Phone}\n  Purpose : {Purpose}\n  Message : {Message}",
            phone, message.Purpose, message.Text);

        return Task.FromResult(true);
    }
}
