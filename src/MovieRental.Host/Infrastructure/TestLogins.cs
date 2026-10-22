using System.Security.Cryptography;
using System.Text.Json;

namespace MovieRental.Host.Infrastructure;

/// <summary>
/// The passwords (and the staff PIN) of the test logins and the demo people. They used to be
/// written in the repository, which is public; now each machine draws its own once, at random,
/// into %LOCALAPPDATA%\WatchingYou\test-logins.json, outside the repository. The test databases
/// are seeded from it and the checks in tools\ sign in with it.
/// </summary>
public sealed record TestLogins(string Admin, string Security, string Customer, string Kassa, string Demo, string StaffPin)
{
    public static string FilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WatchingYou", "test-logins.json");

    private static readonly Lazy<TestLogins> Loaded = new(LoadOrCreate);
    public static TestLogins Current => Loaded.Value;

    private static TestLogins LoadOrCreate()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        if (File.Exists(FilePath) && JsonSerializer.Deserialize<TestLogins>(File.ReadAllText(FilePath), options) is { } saved
            && new[] { saved.Admin, saved.Security, saved.Customer, saved.Kassa, saved.Demo, saved.StaffPin }.All(v => !string.IsNullOrEmpty(v)))
            return saved;

        var made = new TestLogins(Password(), Password(), Password(), Password(), Password(),
            RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString());
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(made, options));
        return made;
    }

    /// <summary>Passes the sign-up rules: a capital letter, a digit, 8+ characters.</summary>
    private static string Password()
    {
        const string letters = "abcdefghijkmnpqrstuvwxyz23456789";
        return "Wy" + RandomNumberGenerator.GetString(letters, 12) + "7";
    }
}
