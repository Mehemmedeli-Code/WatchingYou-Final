using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Cinema.Features;

// The advertising boards on the catalogue page: what visitors see, and what the admin changes.

/// <param name="PercentOff">From the board's promo code, and only while that code can be used:
/// a board never promises a discount the checkout would refuse.</param>
/// <param name="Showing">Active and in season today (Baku): on the site right now.</param>
public sealed record BannerItem(
    Guid Id, string Theme, bool IsActive, int SortOrder, string? Headline, string? Subline,
    string? PromoCode, decimal? PercentOff, decimal? AmountOff, DateOnly? StartsOn, DateOnly? EndsOn, bool Showing);

public sealed record GetBannersQuery(bool All) : IQuery<IReadOnlyList<BannerItem>>;

internal sealed class GetBannersHandler(CinemaDbContext db) : IQueryHandler<GetBannersQuery, IReadOnlyList<BannerItem>>
{
    public async Task<IReadOnlyList<BannerItem>> Handle(GetBannersQuery query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now.AddHours(4));
        var banners = await db.PromoBanners.AsNoTracking().OrderBy(b => b.SortOrder).ThenBy(b => b.Theme).ToListAsync(ct);
        var codes = banners.Select(b => b.PromoCode).OfType<string>().ToArray();
        var promos = await db.PromoCodes.AsNoTracking().Where(p => codes.Contains(p.Code)).ToDictionaryAsync(p => p.Code, ct);

        var items = banners.Select(b =>
        {
            var promo = b.PromoCode is { } code ? promos.GetValueOrDefault(code) : null;
            var usable = promo is { IsActive: true }
                && (promo.ValidFromUtc is null || promo.ValidFromUtc <= now)
                && (promo.ValidUntilUtc is null || now < promo.ValidUntilUtc)
                && (promo.MaxRedemptions is null || promo.Redemptions < promo.MaxRedemptions);
            return new BannerItem(b.Id, b.Theme.ToString(), b.IsActive, b.SortOrder, b.Headline, b.Subline,
                usable || query.All ? b.PromoCode : null,
                usable || query.All ? promo?.PercentOff : null, usable || query.All ? promo?.AmountOff : null,
                b.StartsOn, b.EndsOn, b.IsActive && b.InSeason(today));
        });

        return [.. query.All ? items : items.Where(i => i.Showing)];
    }
}

public sealed record UpdateBannerCommand(
    [property: JsonIgnore] Guid Id, bool IsActive, int SortOrder, string? Headline, string? Subline, string? PromoCode,
    DateOnly? StartsOn, DateOnly? EndsOn) : ICommand<Result>;

internal sealed class UpdateBannerValidator : AbstractValidator<UpdateBannerCommand>
{
    public UpdateBannerValidator()
    {
        RuleFor(x => x.Headline).MaximumLength(80);
        RuleFor(x => x.Subline).MaximumLength(140);
        RuleFor(x => x.PromoCode).MaximumLength(32);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 99);
        RuleFor(x => x).Must(x => x.StartsOn is null || x.EndsOn is null || x.StartsOn <= x.EndsOn)
            .WithMessage("The last day cannot be before the first.");
    }
}

internal sealed class UpdateBannerHandler(CinemaDbContext db, IAuditLog audit) : ICommandHandler<UpdateBannerCommand, Result>
{
    public async Task<Result> Handle(UpdateBannerCommand command, CancellationToken ct)
    {
        var banner = await db.PromoBanners.FirstOrDefaultAsync(b => b.Id == command.Id, ct);
        if (banner is null) return Result.Failure(Error.NotFound("Banner"));

        var code = string.IsNullOrWhiteSpace(command.PromoCode) ? null : PromoCode.Normalise(command.PromoCode);
        if (code is not null && banner.Theme == BannerTheme.Brand)
            return Result.Failure(Error.Validation("The WatchingYou board advertises the site, not a discount."));
        if (code is not null && !await db.PromoCodes.AnyAsync(p => p.Code == code, ct))
            return Result.Failure(Error.Validation("There is no promo code with that name."));

        banner.IsActive = command.IsActive;
        banner.SortOrder = command.SortOrder;
        banner.Headline = string.IsNullOrWhiteSpace(command.Headline) ? null : command.Headline.Trim();
        banner.Subline = string.IsNullOrWhiteSpace(command.Subline) ? null : command.Subline.Trim();
        banner.PromoCode = code;
        banner.StartsOn = command.StartsOn;
        banner.EndsOn = command.EndsOn;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry("banner.updated", banner.Theme.ToString(),
            $"{(banner.IsActive ? "on" : "off")} {banner.StartsOn:yyyy-MM-dd}..{banner.EndsOn:yyyy-MM-dd} {banner.PromoCode}", banner.Id), ct);
        return Result.Success();
    }
}

public static class BannerEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/banners", async (IDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.Ask(new GetBannersQuery(false), ct)))
            .WithName("GetBanners").WithTags("Cinema").AllowAnonymous()
            .WithSummary("The advertising boards showing today");

        var admin = app.MapGroup("/api/admin/banners").WithTags("Cinema admin").RequireAuthorization(AppRoles.Admin);
        admin.MapGet("", async (IDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.Ask(new GetBannersQuery(true), ct)))
            .WithName("GetAdminBanners")
            .WithSummary("Every advertising board, on or off");
        admin.MapPut("/{id:guid}", async Task<Results<NoContent, NotFound<Error>, BadRequest<Error>>> (
                Guid id, UpdateBannerCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { Id = id }, ct);
                if (result.IsSuccess) return TypedResults.NoContent();
                return result.Error.Code == "not_found" ? TypedResults.NotFound(result.Error) : TypedResults.BadRequest(result.Error);
            })
            .WithName("UpdateBanner")
            .WithSummary("Change an advertising board")
            .WithDescription("`headline` up to 80, `subline` up to 140 characters, `sortOrder` 0-99, an optional `promoCode` that exists, " +
                             "and the days it shows (`startsOn`, `endsOn`).");
    }
}
