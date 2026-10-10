using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MovieRental.Modules.Rentals.Domain;
using MovieRental.Modules.Rentals.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Payments;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Rentals.Features;

// Watching PRO — $5 for a month of every film. See RentalPricing for the whole price list.

public sealed record ProPayment(Guid Id, DateTime PaidAtUtc, DateTime StartsAtUtc, DateTime EndsAtUtc,
    decimal Amount, string Currency, string CardBrand, string CardLast4);

public sealed record ProStatus(
    string Plan, bool Active, DateTime? EndsAtUtc, int DaysLeft,
    decimal MonthlyPrice, decimal RentalPrice, int RentalDays, string Currency,
    IReadOnlyList<ProPayment> Payments);

public sealed record GetProStatusQuery : IQuery<ProStatus>;

internal sealed class GetProStatusHandler(RentalsDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetProStatusQuery, ProStatus>
{
    public async Task<ProStatus> Handle(GetProStatusQuery query, CancellationToken ct)
    {
        var userId = currentUser.RequireId();
        var now = DateTime.UtcNow;

        var rows = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.EndsAtUtc)
            .ToListAsync(ct);

        // Paid-ahead months join up, so "until" is the end of the latest one — as long as
        // there is no gap between now and it.
        var active = rows.Any(s => s.IsActiveAt(now));
        DateTime? until = active ? rows.Max(s => s.EndsAtUtc) : null;

        return new ProStatus(
            RentalPricing.PlanName, active, until,
            until is { } end ? (int)Math.Ceiling((end - now).TotalDays) : 0,
            RentalPricing.MonthlyPrice, RentalPricing.PeriodPrice, RentalPricing.PeriodDays, RentalPricing.Currency,
            [.. rows.Select(s => new ProPayment(s.Id, s.CreatedAtUtc, s.StartsAtUtc, s.EndsAtUtc,
                s.Amount, s.Currency, s.CardBrand, s.CardLast4))]);
    }
}

// For Admin and Security: everyone who has paid for Watching PRO, and where each stands.
public sealed record ProSubscriberRow(
    Guid UserId, string Name, string Email, bool Active, DateTime FirstPaidAtUtc, DateTime EndsAtUtc,
    int DaysLeft, int Months, decimal TotalPaid, string LastCard, DateTime? ReminderSentAtUtc);

public sealed record ProOverview(
    int Active, int EndingIn3Days, int Lapsed, decimal RevenueThisMonth, decimal RevenueTotal,
    string Currency, IReadOnlyList<ProSubscriberRow> Subscribers);

public sealed record GetProOverviewQuery : IQuery<ProOverview>;

internal sealed class GetProOverviewHandler(RentalsDbContext db, IUserDirectory users)
    : IQueryHandler<GetProOverviewQuery, ProOverview>
{
    public async Task<ProOverview> Handle(GetProOverviewQuery query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var all = await db.Subscriptions.AsNoTracking().ToListAsync(ct);

        var contacts = await users.GetContactsAsync([.. all.Select(s => s.UserId).Distinct()], ct);
        var rows = new List<ProSubscriberRow>();
        foreach (var group in all.GroupBy(s => s.UserId))
        {
            var latest = group.MaxBy(s => s.EndsAtUtc)!;
            var contact = contacts.GetValueOrDefault(group.Key);
            var active = group.Any(s => s.IsActiveAt(now));
            rows.Add(new ProSubscriberRow(
                group.Key, contact?.FullName ?? "—", contact?.Email ?? "—", active,
                group.Min(s => s.CreatedAtUtc), latest.EndsAtUtc,
                active ? (int)Math.Ceiling((latest.EndsAtUtc - now).TotalDays) : 0,
                group.Count(), group.Sum(s => s.Amount), $"{latest.CardBrand} •••• {latest.CardLast4}",
                latest.ReminderSentAtUtc));
        }

        return new ProOverview(
            rows.Count(r => r.Active),
            rows.Count(r => r.Active && r.EndsAtUtc <= now.AddDays(3)),
            rows.Count(r => !r.Active),
            all.Where(s => s.CreatedAtUtc >= monthStart).Sum(s => s.Amount),
            all.Sum(s => s.Amount),
            RentalPricing.Currency,
            [.. rows.OrderByDescending(r => r.Active).ThenBy(r => r.EndsAtUtc)]);
    }
}

public sealed record ProCard(string Number, int ExpiryMonth, int ExpiryYear, string Cvc, string HolderName);

public sealed record SubscribeProCommand(ProCard Card) : ICommand<Result<ProStatus>>;

internal sealed class SubscribeProValidator : AbstractValidator<SubscribeProCommand>
{
    public SubscribeProValidator()
    {
        RuleFor(x => x.Card).NotNull();
        RuleFor(x => x.Card.HolderName).NotEmpty().MaximumLength(100).When(x => x.Card is not null);
    }
}

internal sealed class SubscribeProHandler(
    RentalsDbContext db, ICurrentUser currentUser, IDispatcher dispatcher, IEmailSender email, IUserDirectory users,
    IConfiguration configuration)
    : ICommandHandler<SubscribeProCommand, Result<ProStatus>>
{
    public async Task<Result<ProStatus>> Handle(SubscribeProCommand command, CancellationToken ct)
    {
        var userId = currentUser.RequireId();

        var paid = CardPayment.Check(command.Card, configuration);
        if (paid.IsFailure) return Result.Failure<ProStatus>(paid.Error);
        var (brand, last4) = paid.Value;

        // A month paid while one is still running starts where that one ends: paying early
        // never costs days.
        // One purchase per person at a time. Two sent together (a double tap) both read the
        // same end date, so both months started now and the second payment bought nothing.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lockName = $"pro:{userId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC sp_getapplock @Resource = {lockName}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000", ct);

        var now = DateTime.UtcNow;
        var runningUntil = await db.Subscriptions
            .Where(s => s.UserId == userId && s.EndsAtUtc > now)
            .Select(s => (DateTime?)s.EndsAtUtc)
            .MaxAsync(ct);
        var starts = runningUntil ?? now;

        db.Subscriptions.Add(new Subscription
        {
            UserId = userId,
            Plan = RentalPricing.PlanName,
            StartsAtUtc = starts,
            EndsAtUtc = starts.AddMonths(1),
            Amount = RentalPricing.MonthlyPrice,
            Currency = RentalPricing.Currency,
            CardBrand = brand,
            CardLast4 = last4,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var contact = await users.GetContactAsync(userId, ct);
        if (contact is not null)
            await email.SendAsync(new EmailRequest(contact.Email, $"{RentalPricing.PlanName} abunəliyiniz aktivləşdirildi",
                "<div style=\"font-family:Arial,sans-serif;max-width:560px;color:#111;line-height:1.6\">" +
                $"<p>Hörmətli {System.Net.WebUtility.HtmlEncode(contact.FullName)},</p>" +
                $"<p>{RentalPricing.PlanName} abunəliyiniz uğurla aktivləşdirildi. " +
                $"<strong>{starts.AddMonths(1):dd.MM.yyyy}</strong> tarixinədək bütün filmlərə limitsiz girişiniz var.</p>" +
                $"<p>Ödəniş: ${RentalPricing.MonthlyPrice:0.00}, {brand} •••• {last4}.<br>" +
                "Abunəlik avtomatik yenilənmir və kart məlumatlarınız saxlanılmır.</p>" +
                "<p>Hörmətlə,<br>WatchingYou komandası</p>" +
                "<hr style=\"border:none;border-top:1px solid #ddd;margin:24px 0 12px\">" +
                $"<p style=\"color:#666;font-size:12px\"><strong>In English:</strong> Your {RentalPricing.PlanName} subscription is active " +
                $"until {starts.AddMonths(1):dd MMM yyyy}. It does not renew automatically and your card details are not stored.</p></div>"), ct);

        return Result.Success(await dispatcher.Ask(new GetProStatusQuery(), ct));
    }
}

/// <summary>
/// A card payment as this module takes one: the test card of the seat checkout, which never
/// charges, so it is refused on a live site (Payments:AllowTestCard). Only the brand and the
/// last four digits leave here; the number and CVC are never stored.
/// </summary>
internal static class CardPayment
{
    public static Result<(string Brand, string Last4)> Check(ProCard? card, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Payments:AllowTestCard"))
            return Result.Failure<(string, string)>(Error.Validation("Card payment is not available yet."));
        if (card is null)
            return Result.Failure<(string, string)>(Error.Validation("Enter the card details."));

        var digits = CardRules.Digits(card.Number);
        var brand = CardRules.BrandName(digits);
        if (digits.Length != 16 || !CardRules.PassesLuhn(digits))
            return Result.Failure<(string, string)>(Error.Validation("The card number is not valid."));
        if (brand is null)
            return Result.Failure<(string, string)>(Error.Validation("Only Visa and Mastercard are accepted."));
        if (!CardRules.ExpiryIsFuture(card.ExpiryMonth, card.ExpiryYear))
            return Result.Failure<(string, string)>(Error.Validation("The card has expired."));
        if (!CardRules.CvcLooksRight(card.Cvc))
            return Result.Failure<(string, string)>(Error.Validation("The CVC is three digits."));
        if (string.IsNullOrWhiteSpace(card.HolderName))
            return Result.Failure<(string, string)>(Error.Validation("Enter the name on the card."));
        return Result.Success((brand, digits[^4..]));
    }
}

public static class WatchingProEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var pro = app.MapGroup("/api/pro").WithTags("Watching PRO").RequireAuthorization();

        pro.MapGet("", async (IDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.Ask(new GetProStatusQuery(), ct)))
            .WithName("GetProStatus");

        pro.MapPost("/subscribe",
            async Task<Results<Ok<ProStatus>, BadRequest<Error>>> (
                SubscribeProCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(command, ct);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.BadRequest(result.Error);
            })
            .WithName("SubscribePro");

        app.MapGet("/api/pro/subscribers", async (IDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.Ask(new GetProOverviewQuery(), ct)))
            .WithName("GetProSubscribers").WithTags("Watching PRO")
            .RequireAuthorization(AppPolicies.SecurityDesk);
    }
}
