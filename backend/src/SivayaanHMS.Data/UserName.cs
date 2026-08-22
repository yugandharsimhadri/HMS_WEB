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
public static class UserName
{
    public const char Separator = '@';

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
