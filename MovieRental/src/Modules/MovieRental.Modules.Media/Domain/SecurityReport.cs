using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Media.Domain;

public enum SecurityCheck
{
    Copyright = 1,
    SexualContent = 2,
    GraphicViolence = 3,
    MinorsWithoutConsent = 4,
    HateOrExtremism = 5,
    PersonalData = 6,
    FileIntegrity = 7,
    OriginDeclaration = 8
}

public enum CheckOutcome { Pass = 1, Fail = 2, NotApplicable = 3 }

public enum SecurityVerdict { Cleared = 1, Flagged = 2 }

/// <summary>
/// The inspection itself, kept as data rather than a free-text note. Storing each check
/// separately is what lets the admin queue show *why* something was flagged, and what lets
/// the rejection e-mail name the specific failure instead of saying "not accepted".
/// </summary>
public sealed class SecurityReport : BaseEntity
{
    public Guid ShortFilmId { get; set; }
    public ShortFilm? ShortFilm { get; set; }

    public Guid ReviewerUserId { get; set; }
    public required string ReviewerName { get; set; }

    public SecurityVerdict Verdict { get; set; }
    public string? Summary { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>True once the reviewer confirms they watched it end to end.</summary>
    public bool WatchedInFull { get; set; }

    public List<SecurityCheckResult> Checks { get; set; } = [];

    public IEnumerable<SecurityCheckResult> Failures => Checks.Where(c => c.Outcome == CheckOutcome.Fail);
}

public sealed class SecurityCheckResult : BaseEntity
{
    public Guid SecurityReportId { get; set; }
    public SecurityReport? Report { get; set; }

    public SecurityCheck Check { get; set; }
    public CheckOutcome Outcome { get; set; }
    public string? Note { get; set; }
}
