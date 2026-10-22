using System.Security.Cryptography;
using System.Text;

namespace MovieRental.Modules.Cinema.Infrastructure;

/// <summary>
/// Salted SHA-256 for the six-digit booking code. Same reasoning as account verification:
/// the code lives fifteen minutes and dies after five attempts, so a slow hash buys little,
/// while a plain code sitting in a table is usable by anyone who can read it.
/// </summary>
internal static class CodeHasher
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
