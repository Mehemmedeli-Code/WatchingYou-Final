using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MovieRental.Modules.Identity.Domain;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Infrastructure;

public interface ITokenService
{
    AccessToken CreateAccessToken(AppUser user);
    (RefreshTokenEntity Entity, string Value) CreateRefreshToken(Guid userId, string? ip);
    string CreateNumericCode(int digits = 6);
}

public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);

public sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;
    // JsonWebTokenHandler is the current Microsoft.IdentityModel API (JwtSecurityTokenHandler is
    // legacy), and the same one the JwtBearer middleware validates with.
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateAccessToken(AppUser user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new("email_confirmed", user.IsEmailConfirmed.ToString().ToLowerInvariant()),
            // Checked on every request (AuthCookie.IsCurrentAsync): a password or role change
            // retires the token at once.
            new(AuthCookie.StampClaimType, AuthCookie.StampOf(user))
        };
        claims.AddRange(user.RoleList.Select(role => new Claim(ClaimTypes.Role, role)));

        var credentials = new SigningCredentials(SigningKey(_options.SecretKey), SecurityAlgorithms.HmacSha256);
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = DateTime.UtcNow,
            Expires = expires,
            SigningCredentials = credentials
        });

        return new AccessToken(token, expires);
    }

    public (RefreshTokenEntity Entity, string Value) CreateRefreshToken(Guid userId, string? ip)
    {
        var value = System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(48));
        return (new RefreshTokenEntity
        {
            UserId = userId,
            Token = HashRefreshToken(value),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_options.RefreshTokenDays),
            CreatedByIp = ip
        }, value);
    }

    /// <summary>The table keeps a hash, never the token: a copy of the database must not be a
    /// set of working sign-ins. SHA-256 is enough — the token is 384 random bits, not a password.</summary>
    public static string HashRefreshToken(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    public string CreateNumericCode(int digits = 6)
    {
        var max = (int)Math.Pow(10, digits);
        return RandomNumberGenerator.GetInt32(max).ToString(new string('0', digits));
    }

    public static SymmetricSecurityKey SigningKey(string secret) =>
        new(System.Text.Encoding.UTF8.GetBytes(secret));
}
