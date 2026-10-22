namespace MovieRental.Modules.Identity.Infrastructure;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "MovieRental";
    public string Audience { get; set; } = "MovieRental.Client";
    public string SecretKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
    public int VerificationCodeMinutes { get; set; } = 10;
}
