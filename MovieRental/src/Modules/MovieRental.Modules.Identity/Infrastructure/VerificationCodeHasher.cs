using System.Security.Cryptography;
using System.Text;

namespace MovieRental.Modules.Identity.Infrastructure;

/// <summary>
/// SHA-256 with a per-code salt rather than BCrypt. The code lives ten minutes and dies
/// after five attempts, so the slow-hash defence against offline brute force buys little,
/// and verification sits on the login path where latency is felt.
/// </summary>
internal static class VerificationCodeHasher
{
    public static (string Hash, string Salt) Create(string code)
    {
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return (Hash(code, salt), salt);
    }

    public static bool Verify(string code, string hash, string salt) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(code, salt)),
            Encoding.UTF8.GetBytes(hash));

    private static string Hash(string code, string salt) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + code)));
}
