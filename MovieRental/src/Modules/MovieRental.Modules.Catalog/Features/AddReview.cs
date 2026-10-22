using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Catalog.Domain;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Catalog.Features;

// Feature 7 — star rating plus written review, one per customer per title.
public sealed record AddReviewCommand(Guid MovieId, int Stars, string Comment) : ICommand<Result<ReviewResponse>>;

internal sealed class AddReviewValidator : AbstractValidator<AddReviewCommand>
{
    public AddReviewValidator()
    {
        RuleFor(x => x.Stars).InclusiveBetween(1, 5);
        RuleFor(x => x.Comment).MaximumLength(2000);
    }
}

internal sealed class AddReviewHandler(CatalogDbContext db, ICurrentUser currentUser, IUserDirectory users)
    : ICommandHandler<AddReviewCommand, Result<ReviewResponse>>
{
    public async Task<Result<ReviewResponse>> Handle(AddReviewCommand command, CancellationToken ct)
    {
        var userId = currentUser.RequireId();
        var movie = await db.Movies.Include(m => m.Reviews).FirstOrDefaultAsync(m => m.Id == command.MovieId, ct);
        if (movie is null) return Result.Failure<ReviewResponse>(Error.NotFound("Movie"));

        var existing = movie.Reviews.FirstOrDefault(r => r.UserId == userId);
        var contact = await users.GetContactAsync(userId, ct);
        var authorName = contact?.FullName ?? currentUser.Email ?? "Anonymous";

        Review review;
        if (existing is not null)
        {
            existing.Stars = command.Stars;
            existing.Comment = command.Comment.Trim();
            review = existing;
        }
        else
        {
            review = new Review
            {
                MovieId = movie.Id,
                UserId = userId,
                AuthorName = authorName,
                Stars = command.Stars,
                Comment = command.Comment.Trim()
            };
            movie.Reviews.Add(review);
        }

        // Recompute in the same transaction as the write, so the denormalised average is
        // never observably out of step with the reviews it summarises.
        movie.ReviewCount = movie.Reviews.Count;
        movie.AverageRating = Math.Round(movie.Reviews.Average(r => (double)r.Stars), 2);

        await db.SaveChangesAsync(ct);
        return Result.Success(new ReviewResponse(review.Id, review.UserId, review.AuthorName,
            review.Stars, review.Comment, review.CreatedAtUtc));
    }
}

public static class AddReviewEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/movies/{id:guid}/reviews",
            async Task<Results<Ok<ReviewResponse>, NotFound<Error>>> (
                Guid id, AddReviewCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { MovieId = id }, ct);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.NotFound(result.Error);
            })
        .WithName("AddReview").WithTags("Catalog").RequireAuthorization();
}
