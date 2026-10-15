using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

public sealed record GetMyProfileQuery : IQuery<UserProfileResponse?>;

internal sealed class GetMyProfileHandler(IdentityDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetMyProfileQuery, UserProfileResponse?>
{
    public async Task<UserProfileResponse?> Handle(GetMyProfileQuery query, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUser.RequireId(), ct);
        return user?.ToProfile();
    }
}

public static class CurrentUserProfileEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/auth/me",
            async Task<Results<Ok<UserProfileResponse>, NotFound<Error>>> (IDispatcher dispatcher, CancellationToken ct) =>
            {
                var profile = await dispatcher.Ask(new GetMyProfileQuery(), ct);
                return profile is null ? TypedResults.NotFound(Error.NotFound("Account")) : TypedResults.Ok(profile);
            })
        .WithName("GetMyProfile").WithTags("Auth").RequireAuthorization()
        .WithSummary("Who am I: the signed-in account")
        .WithDescription("Works with the access token (Authorization: Bearer) or the website's session cookie. " +
                         "404: the account has been deleted since the token was issued.");
}
