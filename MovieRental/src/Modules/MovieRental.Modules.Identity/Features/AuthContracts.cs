namespace MovieRental.Modules.Identity.Features;

public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    UserProfileResponse User);

/// <summary>Registration no longer hands out tokens. The account exists but is unusable
/// until the e-mail behind it is proven, so the caller gets a destination, not a session.</summary>
public sealed record RegistrationResponse(string Email, bool VerificationSent, string Message);

public sealed record UserProfileResponse(
    Guid Id, string FullName, string Email, string? PhoneNumber,
    bool IsEmailConfirmed, bool IsPhoneConfirmed, string[] Roles);
