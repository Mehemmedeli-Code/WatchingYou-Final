using System.Text;

namespace MovieRental.SharedKernel.Security;

/// <summary>
/// URL-safe Base64 used for e-mail confirmation links and opaque refresh-token payloads.
/// Standard Base64 breaks in query strings because of '+', '/' and '='.
/// </summary>
public static class Base64UrlText
{
    public static string Encode(string plainText) => Encode(Encoding.UTF8.GetBytes(plainText));

    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Decode(string encoded) => Encoding.UTF8.GetString(DecodeBytes(encoded));

    public static byte[] DecodeBytes(string encoded)
    {
        var normalized = encoded.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(normalized);
    }

    public static bool TryDecode(string encoded, out string plainText)
    {
        try { plainText = Decode(encoded); return true; }
        catch { plainText = string.Empty; return false; }
    }
}
