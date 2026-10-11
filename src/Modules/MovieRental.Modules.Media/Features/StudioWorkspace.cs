using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
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
        if (!Enum.IsDefined(command.Visibility)) return Result.Failure(Error.Validation("Choose public or private."));
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
        if (command.Body.Trim().Length > 2000)
            return Result.Failure<SubmissionCommentDto>(Error.Validation("A comment can be 2000 characters at most."));

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
            async Task<Results<NoContent, NotFound<Error>, BadRequest<Error>, ForbidHttpResult>> (
                Guid id, SetVisibilityCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { Id = id }, ct);
                if (result.IsSuccess) return TypedResults.NoContent();
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "validation" => TypedResults.BadRequest(result.Error),
                    _ => TypedResults.Forbid()
                };
            }).WithName("SetShortFilmVisibilityWithId");

        // The uploader takes back a film that has not been approved yet — the wrong file, a
        // change of mind. The record is soft-deleted like an admin removal, but the file goes:
        // nobody should keep a video its owner withdrew by mistake.
        mine.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound, BadRequest<Error>>> (
            Guid id, MediaDbContext db, ICurrentUser currentUser, IHostEnvironment environment, CancellationToken ct) =>
        {
            var film = await db.ShortFilms.FirstOrDefaultAsync(f => f.Id == id && f.UserId == currentUser.RequireId(), ct);
            if (film is null) return TypedResults.NotFound();
            if (film.Status == SubmissionStatus.Approved)
                return TypedResults.BadRequest(Error.Validation("An approved film cannot be withdrawn here."));

            db.ShortFilms.Remove(film);
            await db.SaveChangesAsync(ct);
            var path = ShortFilmStorage.PathFor(environment, film.StoredFileName);
            if (File.Exists(path)) File.Delete(path);
            return TypedResults.NoContent();
        }).WithName("WithdrawShortFilm");

        mine.MapPost("/{id:guid}/comments",
            async Task<Results<Ok<SubmissionCommentDto>, BadRequest<Error>, NotFound<Error>>> (
                Guid id, AddSubmissionCommentCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { Id = id }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "not_found"
                    ? TypedResults.NotFound(result.Error)
                    : TypedResults.BadRequest(result.Error);
            }).WithName("AddSubmissionCommentWithId").RequireRateLimiting(AppPolicies.WriteRateLimit);
    }
}
