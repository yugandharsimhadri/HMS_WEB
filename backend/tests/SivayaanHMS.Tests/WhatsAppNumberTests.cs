using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SivayaanHMS.Data.Messaging;

namespace SivayaanHMS.Tests;

/// <summary>
/// Turning what a clinic typed into what WhatsApp accepts.
///
/// Worth pinning because this decides *where a password-reset code goes*. A
/// number silently mangled into a different valid number does not fail — it
/// sends somebody else the code, and the account owner just sees "no message
/// arrived".
/// </summary>
public class WhatsAppNumberTests
{
    private static WhatsAppCloudMessageSender Sender(string defaultCountryCode) =>
        new(new HttpClient(),
            Options.Create(new WhatsAppOptions
            {
                AccessToken = "test",
                PhoneNumberId = "test",
                DefaultCountryCode = defaultCountryCode,
            }),
            NullLogger<WhatsAppCloudMessageSender>.Instance);

    [Theory]
    [InlineData("+91 98765 43210", "919876543210")]   // the form the UI suggests
    [InlineData("+917036188912", "917036188912")]     // no spaces
    [InlineData("91 98765-43210", "919876543210")]    // hyphens and no plus
    [InlineData("0091 9876543210", "919876543210")]   // 00 is the other way of writing +
    [InlineData("9876543210", "919876543210")]        // bare national: the default is prepended
    public void Reduces_what_was_typed_to_digits_with_a_country_code(string typed, string expected)
    {
        Assert.True(Sender("91").TryNormalise(typed, out var normalised));
        Assert.Equal(expected, normalised);
    }

    [Fact]
    public void Does_not_prepend_when_the_number_already_carries_that_country_code()
    {
        Assert.True(Sender("91").TryNormalise("919876543210", out var normalised));
        Assert.Equal("919876543210", normalised);
    }

    /// <summary>
    /// The important one. Without a configured country code a ten-digit
    /// national number is refused rather than guessed at — guessing would
    /// send a clinic's reset code to whoever in another country happens to
    /// hold those digits.
    /// </summary>
    [Fact]
    public void Refuses_a_number_too_short_to_carry_a_country_code_when_none_is_configured()
    {
        Assert.False(Sender("").TryNormalise("12345678", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a number")]
    [InlineData("12345678901234567")]  // longer than any real number
    public void Refuses_what_cannot_be_a_phone_number(string? typed)
    {
        Assert.False(Sender("91").TryNormalise(typed, out _));
    }
}
