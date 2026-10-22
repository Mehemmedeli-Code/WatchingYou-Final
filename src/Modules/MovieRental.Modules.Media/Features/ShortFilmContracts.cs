using Microsoft.Extensions.Hosting;
using MovieRental.Modules.Media.Domain;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Media.Features;

public sealed record ShortFilmSummary(
    Guid Id, string Title, string Synopsis, string AuthorName,
    ShortFilmOrigin Origin, ShortFilmVisibility Visibility, SubmissionStatus Status,
    DateTime SubmittedAtUtc, DateTime ReviewDeadlineUtc, DateTime? ApprovedAtUtc,
    int ViewCount, long SizeBytes, string OriginalFileName, string ContentType,
    int HoursLeft, string? ReviewerNote, string StreamUrl);

public sealed record SecurityCheckDto(SecurityCheck Check, CheckOutcome Outcome, string? Note);

public sealed record SecurityReportDto(
    string ReviewerName, SecurityVerdict Verdict, string? Summary, bool WatchedInFull,
    DateTime CompletedAtUtc, IReadOnlyList<SecurityCheckDto> Checks);

public sealed record SubmissionCommentDto(
    Guid Id, string AuthorName, string AuthorRole, string Body, DateTime CreatedAtUtc);

/// <summary>What Studio, Security and Admin all render — the film plus its full review trail.</summary>
public sealed record ShortFilmDetail(
    ShortFilmSummary Film, SecurityReportDto? Report, IReadOnlyList<SubmissionCommentDto> Comments);

internal static class ShortFilmMapper
{
    public static ShortFilmSummary ToSummary(this ShortFilm f, DateTime nowUtc) => new(
        f.Id, f.Title, f.Synopsis, f.AuthorName, f.Origin, f.Visibility, f.Status,
        f.SubmittedAtUtc, f.ReviewDeadlineUtc, f.ApprovedAtUtc, f.ViewCount, f.SizeBytes,
        f.OriginalFileName, f.ContentType, f.HoursLeft(nowUtc), f.ReviewerNote,
        $"/api/shorts/{f.Id}/stream");

    public static SecurityReportDto ToDto(this SecurityReport r) => new(
        r.ReviewerName, r.Verdict, r.Summary, r.WatchedInFull, r.CompletedAtUtc,
        [.. r.Checks.OrderBy(c => c.Check).Select(c => new SecurityCheckDto(c.Check, c.Outcome, c.Note))]);

    public static SubmissionCommentDto ToDto(this SubmissionComment c) => new(
        c.Id, c.AuthorName, c.AuthorRole, c.Body, c.CreatedAtUtc);
}

internal static class ShortFilmStorage
{
    public static string Folder(IHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "uploads", "shorts");

    public static string PathFor(IHostEnvironment environment, string storedFileName) =>
        Path.Combine(Folder(environment), storedFileName);

    /// <summary>The type a stored film is served with, from its own (server-chosen) extension.
    /// The uploader's Content-Type was stored and served back before, so a "film" sent as
    /// text/html ran as a page on this origin for whoever opened it.</summary>
    public static string ContentTypeFor(string storedFileName) => Path.GetExtension(storedFileName).ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".mkv" => "video/x-matroska",
        _ => "application/octet-stream"
    };
}

internal static class ViewerRules
{
    /// <summary>
    /// Whether an approved film is out in its gallery, for everyone. On a public account every
    /// approved film is: there is nothing to hide, anyone can open the profile anyway. On a
    /// private account only the films its owner made public are; the rest are for followers.
    /// </summary>
    public static bool IsListed(ShortFilm film, bool ownerIsPrivate) =>
        film.Status == SubmissionStatus.Approved && (!ownerIsPrivate || film.Visibility == ShortFilmVisibility.Public);

    /// <summary>Owner, Security and Admin may always watch. Anyone may watch a listed film; an
    /// approved film kept for followers, only someone the private account has accepted.</summary>
    public static async Task<bool> MayWatchAsync(ShortFilm film, ICurrentUser user, IUserDirectory users, CancellationToken ct)
    {
        if (user.IsAuthenticated && (user.Id == film.UserId || IsReviewer(user))) return true;
        if (film.Status != SubmissionStatus.Approved) return false;
        if (IsListed(film, (await users.PrivateAccountsAsync([film.UserId], ct)).Contains(film.UserId))) return true;
        return user.IsAuthenticated && await users.MayViewProfileAsync(user.RequireId(), film.UserId, ct);
    }
    public static bool IsReviewer(ICurrentUser user) =>
        user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.Security);

    public static string RoleLabel(ICurrentUser user) =>
        user.IsInRole(AppRoles.Admin) ? AppRoles.Admin
        : user.IsInRole(AppRoles.Security) ? AppRoles.Security
        : "Owner";
}
