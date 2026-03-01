using System.Security.Cryptography;
using System.Text;

namespace TimeGuard.Helpers;

/// <summary>
/// Password hashing using PBKDF2 + SHA-256. Salt and hash are stored as Base64 strings.
/// </summary>
public static class PasswordHelper
{
    private const int SaltBytes    = 32;
    private const int HashBytes    = 32;
    private const int Iterations   = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static (string hash, string salt) Hash(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltBytes);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            saltBytes,
            Iterations,
            Algorithm,
            HashBytes);

        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    public static bool Verify(string password, string storedHash, string storedSalt)
    {
        var saltBytes = Convert.FromBase64String(storedSalt);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            saltBytes,
            Iterations,
            Algorithm,
            HashBytes);

        return CryptographicOperations.FixedTimeEquals(
            hashBytes,
            Convert.FromBase64String(storedHash));
    }
}
