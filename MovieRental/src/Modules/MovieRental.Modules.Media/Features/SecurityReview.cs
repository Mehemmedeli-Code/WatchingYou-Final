using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Media.Domain;
using MovieRental.Modules.Media.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Media.Features;

// Stage one of the review pipeline. Security watches the film and records findings; it
// cannot publish anything. Separating inspection from publication is the point of the role.

public sealed record GetSecurityQueueQuery : IQuery<IReadOnlyList<ShortFilmDetail>>;

internal sealed class GetSecurityQueueHandler(MediaDbContext db)
    : IQueryHandler<GetSecurityQueueQuery, IReadOnlyList<ShortFilmDetail>>
{
    public async Task<IReadOnlyList<ShortFilmDetail>> Handle(GetSecurityQueueQuery query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var films = await db.ShortFilms.AsNoTracking()
            .Include(f => f.SecurityReport!).ThenInclude(r => r.Checks)
            .Include(f => f.Comments)
            .Where(f => f.Status == SubmissionStatus.Pending || f.Status == SubmissionStatus.UnderSecurityReview)
            .OrderBy(f => f.ReviewDeadlineUtc)   // closest to breaching the promise, first
            .ToListAsync(ct);

        return [.. films.Select(f => new ShortFilmDetail(
            f.ToSummary(now), f.SecurityReport?.ToDto(),
            [.. f.Comments.OrderBy(c => c.CreatedAtUtc).Select(c => c.ToDto())]))];
    }
}

/// <summary>Marks a submission as being looked at, so two reviewers do not duplicate work.</summary>
public sealed record ClaimForReviewCommand(Guid Id) : ICommand<Result>;

internal sealed class ClaimForReviewHandler(MediaDbContext db) : ICommandHandler<ClaimForReviewCommand, Result>
{
    public async Task<Result> Handle(ClaimForReviewCommand command, CancellationToken ct)
    {
        var film = await db.ShortFilms.FirstOrDefaultAsync(f => f.Id == command.Id, ct);
        if (film is null) return Result.Failure(Error.NotFound("Submission"));
        if (film.Status != SubmissionStatus.Pending)
            return Result.Failure(Error.Conflict("This submission is no longer waiting for a first look."));

        film.Status = SubmissionStatus.UnderSecurityReview;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record SubmitSecurityReportCommand(
    Guid Id, bool WatchedInFull, string? Summary, IReadOnlyList<SecurityCheckDto> Checks) : ICommand<Result>;

internal sealed class SubmitSecurityReportHandler(
    MediaDbContext db, ICurrentUser currentUser, IUserDirectory users)
    : ICommandHandler<SubmitSecurityReportCommand, Result>
{
    private static readonly SecurityCheck[] Required = Enum.GetValues<SecurityCheck>();

    public async Task<Result> Handle(SubmitSecurityReportCommand command, CancellationToken ct)
    {
        var film = await db.ShortFilms
            .Include(f => f.SecurityReport)
            .FirstOrDefaultAsync(f => f.Id == command.Id, ct);

        if (film is null) return Result.Failure(Error.NotFound("Submission"));
        if (film.IsDecided) return Result.Failure(Error.Conflict("This submission has already been decided."));
        if (film.SecurityReport is not null)
            return Result.Failure(Error.Conflict("A security report already exists for this submission."));

        if (!command.WatchedInFull)
            return Result.Failure(Error.Validation("Confirm you watched the film end to end before filing."));

        var missing = Required.Where(c => command.Checks.All(x => x.Check != c)).ToArray();
        if (missing.Length > 0)
            return Result.Failure(Error.Validation(
                $"Every check needs an outcome. Missing: {string.Join(", ", missing)}."));

        var reviewerId = currentUser.RequireId();
        var contact = await users.GetContactAsync(reviewerId, ct);
        var failed = command.Checks.Any(c => c.Outcome == CheckOutcome.Fail);

        var report = new SecurityReport
        {
            ShortFilmId = film.Id,
            ReviewerUserId = reviewerId,
            ReviewerName = contact?.FullName ?? "Security",
            WatchedInFull = true,
            Summary = command.Summary?.Trim(),
            Verdict = failed ? SecurityVerdict.Flagged : SecurityVerdict.Cleared,
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            Checks = [.. command.Checks.Select(c => new SecurityCheckResult
            {
                Check = c.Check,
                Outcome = c.Outcome,
                Note = c.Note?.Trim()
            })]
        };

        db.SecurityReports.Add(report);
        film.Status = failed ? SubmissionStatus.SecurityFlagged : SubmissionStatus.SecurityCleared;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class SecurityReviewEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // Admin can see the Security desk as well; the reverse is deliberately not true.
        var desk = app.MapGroup("/api/security/shorts").WithTags("Security")
            .RequireAuthorization(AppPolicies.SecurityDesk);

        desk.MapGet("/queue", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetSecurityQueueQuery(), ct)))
            .WithName("GetSecurityQueue");

        desk.MapPost("/{id:guid}/claim",
            async Task<Results<NoContent, Conflict<Error>, NotFound<Error>>> (
                Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new ClaimForReviewCommand(id), ct);
                if (result.IsSuccess) return TypedResults.NoContent();
                return result.Error.Code == "not_found"
                    ? TypedResults.NotFound(result.Error)
                    : TypedResults.Conflict(result.Error);
            }).WithName("ClaimShortFilmForReviewWithId");

        desk.MapPost("/{id:guid}/report",
            async Task<Results<NoContent, BadRequest<Error>, Conflict<Error>, NotFound<Error>>> (
                Guid id, SubmitSecurityReportCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { Id = id }, ct);
                if (result.IsSuccess) return TypedResults.NoContent();
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "conflict" => TypedResults.Conflict(result.Error),
                    _ => TypedResults.BadRequest(result.Error)
                };
            }).WithName("SubmitSecurityReportWithId");
    }
}
