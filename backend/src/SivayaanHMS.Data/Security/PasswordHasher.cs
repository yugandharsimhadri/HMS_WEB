using System.Security.Cryptography;

namespace SivayaanHMS.Data.Security;

/// <summary>
/// PBKDF2-HMACSHA256, built into .NET — no extra package for something this
/// small. A random salt per password and a fixed-time comparison on verify,
/// the two things worth getting right; the iteration count is the one knob
/// that trades login speed for brute-force cost.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public static (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string hash, string salt)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt)) return false;

        try
        {
            var saltBytes = Convert.FromBase64String(salt);
            var expected = Convert.FromBase64String(hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, HashAlgorithmName.SHA256, expected.Length);

            // Not "==" or SequenceEqual: a comparison that returns early on
            // the first mismatched byte leaks how much of the guess was
            // right through how long it took to be told no.
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            // A hash or salt that will not even decode is not a match.
            return false;
        }
    }
}
