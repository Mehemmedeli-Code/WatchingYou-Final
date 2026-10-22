using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Media.Domain;

/// <summary>
/// A private thread between the uploader and the people reviewing their film. Not a public
/// comment section — only the owner, Security and Admin can read or write it.
/// </summary>
public sealed class SubmissionComment : BaseEntity, ISoftDeletable
{
    public Guid ShortFilmId { get; set; }
    public ShortFilm? ShortFilm { get; set; }

    public Guid AuthorUserId { get; set; }
    public required string AuthorName { get; set; }

    /// <summary>Owner, Security or Admin — shown as a badge so the uploader can tell a
    /// reviewer's reply from their own note.</summary>
    public required string AuthorRole { get; set; }

    public required string Body { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
