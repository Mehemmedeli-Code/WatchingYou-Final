namespace MovieRental.SharedKernel.Contracts;

/// <summary>Read-only view of Identity for modules that need to name or e-mail a user.</summary>
public interface IUserDirectory
{
    Task<UserContact?> GetContactAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Contacts for many users in one query. For report and export screens that would
    /// otherwise look each row's e-mail up one at a time (a classic N+1).</summary>
    Task<IReadOnlyDictionary<Guid, UserContact>> GetContactsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    /// <summary>Whether the viewer may see what is on the owner's profile: their own, a public
    /// account's, or a private account that accepted them.</summary>
    Task<bool> MayViewProfileAsync(Guid viewerId, Guid ownerId, CancellationToken ct = default);

    /// <summary>Which of these people have a private account.</summary>
    Task<IReadOnlySet<Guid>> PrivateAccountsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}

public sealed record UserContact(Guid Id, string FullName, string Email, string? PhoneNumber, bool ShareOnGlobe = false);
