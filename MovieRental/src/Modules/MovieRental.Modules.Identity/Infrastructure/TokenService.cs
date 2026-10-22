using System.IdentityModel.Tokens.Jwt;
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
    RefreshTokenEntity CreateRefreshToken(Guid userId, string? ip);
    string CreateNumericCode(int digits = 6);
    string CreateConfirmationLinkPayload(Guid userId, string purpose);
    bool TryReadConfirmationLinkPayload(string payload, out Guid userId, out string purpose);
}

public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);

public sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;
    private readonly JwtSecurityTokenHandler _handler = new();

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
            new("email_confirmed", user.IsEmailConfirmed.ToString().ToLowerInvariant())
        };
        claims.AddRange(user.RoleList.Select(role => new Claim(ClaimTypes.Role, role)));

        var credentials = new SigningCredentials(SigningKey(_options.SecretKey), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: credentials);

        return new AccessToken(_handler.WriteToken(token), expires);
    }

    public RefreshTokenEntity CreateRefreshToken(Guid userId, string? ip) => new()
    {
        UserId = userId,
        Token = Base64UrlText.Encode(RandomNumberGenerator.GetBytes(48)),
        ExpiresAtUtc = DateTime.UtcNow.AddDays(_options.RefreshTokenDays),
        CreatedByIp = ip
    };

    public string CreateNumericCode(int digits = 6)
    {
        var max = (int)Math.Pow(10, digits);
        return RandomNumberGenerator.GetInt32(max).ToString(new string('0', digits));
    }

    public string CreateConfirmationLinkPayload(Guid userId, string purpose) =>
        Base64UrlText.Encode($"{userId:N}|{purpose}|{DateTime.UtcNow:O}");

    public bool TryReadConfirmationLinkPayload(string payload, out Guid userId, out string purpose)
    {
        userId = Guid.Empty;
        purpose = string.Empty;
        if (!Base64UrlText.TryDecode(payload, out var plain)) return false;

        var parts = plain.Split('|');
        if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out userId)) return false;

        purpose = parts[1];
        return DateTime.TryParse(parts[2], out var issuedAt) && DateTime.UtcNow - issuedAt.ToUniversalTime() < TimeSpan.FromDays(2);
    }

    public static SymmetricSecurityKey SigningKey(string secret) =>
        new(System.Text.Encoding.UTF8.GetBytes(secret));
}
