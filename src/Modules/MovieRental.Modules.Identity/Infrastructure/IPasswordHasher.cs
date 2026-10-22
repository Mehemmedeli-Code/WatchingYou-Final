namespace MovieRental.Modules.Identity.Infrastructure;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => BCrypt.Net.BCrypt.EnhancedHashPassword(password, workFactor: 12);

    public bool Verify(string password, string hash)
    {
        try { return BCrypt.Net.BCrypt.EnhancedVerify(password, hash); }
        catch { return false; }
    }
}
