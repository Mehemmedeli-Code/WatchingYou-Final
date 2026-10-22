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

// The uploader's own workspace: watch it back, read the review trail, publish or unpublish,
// and talk to the people reviewing it.

public sealed record GetMySubmissionsQuery : IQuery<IReadOnlyList<ShortFilmDetail>>;

internal sealed class GetMySubmissionsHandler(MediaDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetMySubmissionsQuery, IReadOnlyList<ShortFilmDetail>>
{
    public async Task<IReadOnlyList<ShortFilmDetail>> Handle(GetMySubmissionsQuery query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var films = await db.ShortFilms.AsNoTracking()
            .Include(f => f.SecurityReport!).ThenInclude(r => r.Checks)
            .Include(f => f.Comments)
            .Where(f => f.UserId == currentUser.RequireId())
            .OrderByDescending(f => f.SubmittedAtUtc)
            .ToListAsync(ct);

        return [.. films.Select(f => new ShortFilmDetail(
            f.ToSummary(now),
            f.SecurityReport?.ToDto(),
            [.. f.Comments.OrderBy(c => c.CreatedAtUtc).Select(c => c.ToDto())]))];
    }
}

public sealed record SetVisibilityCommand(Guid Id, ShortFilmVisibility Visibility) : ICommand<Result>;

internal sealed class SetVisibilityHandler(MediaDbContext db, ICurrentUser currentUser)
    : ICommandHandler<SetVisibilityCommand, Result>
{
    public async Task<Result> Handle(SetVisibilityCommand command, CancellationToken ct)
    {
        var film = await db.ShortFilms.FirstOrDefaultAsync(f => f.Id == command.Id, ct);
        if (film is null) return Result.Failure(Error.NotFound("Submission"));
        if (film.UserId != currentUser.RequireId())
            return Result.Failure(Error.Forbidden("This is not your submission."));

        // Allowed at any stage. Going public before approval simply queues the intent —
        // the gallery still filters on Approved, so nothing leaks early.
        film.Visibility = command.Visibility;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record AddSubmissionCommentCommand(Guid Id, string Body) : ICommand<Result<SubmissionCommentDto>>;

internal sealed class AddSubmissionCommentHandler(
    MediaDbContext db, ICurrentUser currentUser, IUserDirectory users)
    : ICommandHandler<AddSubmissionCommentCommand, Result<SubmissionCommentDto>>
{
    public async Task<Result<SubmissionCommentDto>> Handle(AddSubmissionCommentCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Body))
            return Result.Failure<SubmissionCommentDto>(Error.Validation("Write something first."));

        var film = await db.ShortFilms.FirstOrDefaultAsync(f => f.Id == command.Id, ct);
        if (film is null) return Result.Failure<SubmissionCommentDto>(Error.NotFound("Submission"));

        var userId = currentUser.RequireId();
        if (film.UserId != userId && !ViewerRules.IsReviewer(currentUser))
            return Result.Failure<SubmissionCommentDto>(Error.Forbidden("You cannot post on this submission."));

        var contact = await users.GetContactAsync(userId, ct);
        var comment = new SubmissionComment
        {
            ShortFilmId = film.Id,
            AuthorUserId = userId,
            AuthorName = contact?.FullName ?? "Unknown",
            AuthorRole = film.UserId == userId ? "Owner" : ViewerRules.RoleLabel(currentUser),
            Body = command.Body.Trim()
        };

        db.SubmissionComments.Add(comment);
        await db.SaveChangesAsync(ct);
        return Result.Success(comment.ToDto());
    }
}

public static class StudioEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var mine = app.MapGroup("/api/shorts").WithTags("Shorts").RequireAuthorization();

        mine.MapGet("/mine", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetMySubmissionsQuery(), ct)))
            .WithName("GetMyShortFilms");

        mine.MapPut("/{id:guid}/visibility",
            async Task<Results<NoContent, NotFound<Error>, ForbidHttpResult>> (
                Guid id, SetVisibilityCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { Id = id }, ct);
                if (result.IsSuccess) return TypedResults.NoContent();
                return result.Error.Code == "not_found"
                    ? TypedResults.NotFound(result.Error)
                    : TypedResults.Forbid();
            }).WithName("SetShortFilmVisibilityWithId");

        mine.MapPost("/{id:guid}/comments",
            async Task<Results<Ok<SubmissionCommentDto>, BadRequest<Error>, NotFound<Error>>> (
                Guid id, AddSubmissionCommentCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { Id = id }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "not_found"
                    ? TypedResults.NotFound(result.Error)
                    : TypedResults.BadRequest(result.Error);
            }).WithName("AddSubmissionCommentWithId");
    }
}
