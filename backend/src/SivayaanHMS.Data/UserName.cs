using System.Text.RegularExpressions;

namespace SivayaanHMS.Data;

/// <summary>
/// Usernames are <c>local@clinic-slug</c> — "reception@twinkle".
///
/// The suffix does two jobs at once. It makes every username globally
/// unique for free (clinic slugs already are, so no two clinics can both
/// own "admin"), and it lets sign-in resolve which clinic a request is for
/// from the username alone. That is what removes the third box from the
/// login page: a receptionist types what they were given and nothing else,
/// and a person who works at two clinics has two genuinely distinct
/// identities rather than one that needs disambiguating every morning.
///
/// The clinic is asked for once, at registration, and never again.
/// </summary>
public static partial class UserName
{
    public const char Separator = '@';

    /// <summary>
    /// What a person may choose as the part before the clinic. Deliberately
    /// narrower than the separator rule needs: no spaces or capitals, so a
    /// username read aloud over a phone to a locked-out receptionist can only
    /// be typed back one way. The separator itself is excluded — a local part
    /// containing one would still resolve (TrySplit takes the last), but it
    /// reads as an email address and invites people to type their email.
    /// </summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,31}$")]
    private static partial Regex ValidLocalPart();

    /// <summary>
    /// Checks a chosen local part and explains the rule when it fails, rather
    /// than returning a bare false — this is read by someone mid-signup who
    /// needs to know what to type instead.
    /// </summary>
    public static bool TryNormaliseLocalPart(string? raw, out string localPart, out string? error)
    {
        localPart = (raw ?? string.Empty).Trim().ToLowerInvariant();
        error = null;

        if (localPart.Length == 0)
        {
            error = "Choose a username.";
            return false;
        }

        if (localPart.Contains(Separator))
        {
            error = $"Leave the '{Separator}' out — your clinic is added to your username automatically.";
            return false;
        }

        if (!ValidLocalPart().IsMatch(localPart))
        {
            error = "A username is 3–32 characters: lowercase letters, numbers, dots, hyphens or underscores, " +
                    "starting with a letter or number.";
            return false;
        }

        return true;
    }

    /// <summary>Builds the stored username from the part a person chooses
    /// and the clinic they belong to.</summary>
    public static string For(string localPart, string clinicSlug)
        => $"{localPart.Trim().ToLowerInvariant()}{Separator}{clinicSlug.Trim().ToLowerInvariant()}";

    /// <summary>
    /// Splits a typed username back into its two halves. Returns false for
    /// anything without a suffix — which sign-in treats exactly like a wrong
    /// password, because "no such clinic" told to an anonymous caller is a
    /// free directory of every clinic on the platform.
    /// </summary>
    public static bool TrySplit(string? username, out string localPart, out string clinicSlug)
    {
        localPart = clinicSlug = "";

        var trimmed = username?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;

        // Last separator wins, so a local part containing one (an email-style
        // username) still resolves against the clinic on the end.
        var at = trimmed.LastIndexOf(Separator);
        if (at <= 0 || at == trimmed.Length - 1) return false;

        localPart = trimmed[..at].ToLowerInvariant();
        clinicSlug = trimmed[(at + 1)..].ToLowerInvariant();
        return true;
    }
}
