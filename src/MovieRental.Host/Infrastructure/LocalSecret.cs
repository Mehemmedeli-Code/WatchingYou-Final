using System.Security.Cryptography;

namespace MovieRental.Host.Infrastructure;

/// <summary>
/// A random secret made once per machine and kept outside the repository
/// (%LOCALAPPDATA%\WatchingYou), for development runs that have no user secret of their own.
/// </summary>
public static class LocalSecret
{
    public static string LoadOrCreate(string name)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WatchingYou");
        var path = Path.Combine(folder, name);
        if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: >= 64 } existing) return existing;

        Directory.CreateDirectory(folder);
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        File.WriteAllText(path, secret);
        return secret;
    }
}
