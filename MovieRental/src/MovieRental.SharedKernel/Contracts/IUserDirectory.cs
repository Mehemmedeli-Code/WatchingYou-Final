namespace MovieRental.SharedKernel.Contracts;

/// <summary>Read-only view of Identity for modules that need to name or e-mail a user.</summary>
public interface IUserDirectory
{
    Task<UserContact?> GetContactAsync(Guid userId, CancellationToken ct = default);
}

public sealed record UserContact(Guid Id, string FullName, string Email, string? PhoneNumber);
