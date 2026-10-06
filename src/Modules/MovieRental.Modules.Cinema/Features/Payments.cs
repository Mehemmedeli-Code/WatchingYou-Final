using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Infrastructure;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Cinema.Features;

// Everything about money that is not the seat map itself: the price preview, promo codes,
// loyalty points, Stripe Checkout and the PDF ticket.

/// <summary>Works out a booking's price against the database: looks the promo code up and
/// counts the points this customer can actually spend. The arithmetic is Pricing's.</summary>
internal sealed class CheckoutPricing(CinemaDbContext db)
{
    public async Task<(PriceBreakdown Price, Error? Error)> PriceAsync(
        Screening screening, int seats, string? promoCode, bool usePoints, Guid userId, CancellationToken ct)
    {
        PromoCode? promo = null;
        var code = PromoCode.Normalise(promoCode);
        if (code.Length > 0)
        {
            promo = await db.PromoCodes.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code, ct);
            if (promo is null) return (Pricing.Calculate(screening.SeatPrice, seats, null, DateTime.UtcNow, 0, false),
                Error.Validation(Pricing.Explain(PromoRejection.Unknown)));
        }

        var spendable = usePoints ? await LoyaltyLedger.SpendableAsync(db, userId, ct) : 0;
        var price = Pricing.Calculate(screening.SeatPrice, seats, promo, DateTime.UtcNow, spendable, usePoints);

        // A code that does not apply is an error at checkout (the customer typed it expecting
        // a discount) but only a message in the preview.
        return price.PromoRejection != PromoRejection.None
            ? (price, Error.Validation(Pricing.Explain(price.PromoRejection)))
            : (price, null);
    }
}

// ------------------------------------------------------------------ price preview

public sealed record QuoteRequest(int Seats, string? PromoCode, bool UsePoints);

public sealed record CheckoutQuote(
    decimal Subtotal, decimal PromoDiscount, string? PromoMessage, bool PromoApplied,
    int PointsBalance, int PointsUsed, decimal PointsDiscount, decimal Total, int PointsEarned,
    bool StripeEnabled);

public sealed record GetCheckoutQuoteQuery(Guid ScreeningId, QuoteRequest Request) : IQuery<CheckoutQuote?>;

internal sealed class GetCheckoutQuoteHandler(
    CinemaDbContext db, ICurrentUser currentUser, CheckoutPricing pricing, IStripeGateway stripe)
    : IQueryHandler<GetCheckoutQuoteQuery, CheckoutQuote?>
{
    public async Task<CheckoutQuote?> Handle(GetCheckoutQuoteQuery query, CancellationToken ct)
    {
        var userId = currentUser.RequireId();
        var screening = await db.Screenings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == query.ScreeningId, ct);
        if (screening is null) return null;

        var seats = Math.Clamp(query.Request.Seats, 0, 8);
        var (price, error) = await pricing.PriceAsync(
            screening, seats, query.Request.PromoCode, query.Request.UsePoints, userId, ct);
        var balance = await LoyaltyLedger.SpendableAsync(db, userId, ct);

        return new CheckoutQuote(
            price.Subtotal, price.PromoDiscount,
            error?.Message ?? (price.PromoDiscount > 0 ? $"−{price.PromoDiscount:0.00}" : null),
            price.PromoDiscount > 0,
            balance, price.PointsUsed, price.PointsDiscount, price.Total, price.PointsEarned,
            stripe.Enabled);
    }
}

// ------------------------------------------------------------------ Stripe Checkout

public sealed record StripeCheckoutCommand(
    Guid ScreeningId, IReadOnlyList<SeatSelection> Seats, string? PromoCode, bool UsePoints, string ReturnBaseUrl = "")
    : ICommand<Result<StripeCheckoutStarted>>;

public sealed record StripeCheckoutStarted(Guid PaymentId, string Reference, decimal Amount, string Url, DateTime ExpiresAtUtc);

internal sealed class StripeCheckoutValidator : AbstractValidator<StripeCheckoutCommand>
{
    public StripeCheckoutValidator()
    {
        RuleFor(x => x.Seats).NotEmpty().WithMessage("Pick at least one seat.");
        RuleFor(x => x.Seats.Count).LessThanOrEqualTo(8).WithMessage("Eight seats is the limit per booking.");
    }
}

internal sealed class StripeCheckoutHandler(
    CinemaDbContext db, ICurrentUser currentUser, IUserDirectory users, CheckoutPricing pricing, IStripeGateway stripe)
    : ICommandHandler<StripeCheckoutCommand, Result<StripeCheckoutStarted>>
{
    /// <summary>Stripe's Checkout pages refuse anything under about half a dollar.</summary>
    public const decimal MinimumAmount = 1.00m;

    public async Task<Result<StripeCheckoutStarted>> Handle(StripeCheckoutCommand command, CancellationToken ct)
    {
        if (!stripe.Enabled)
            return Result.Failure<StripeCheckoutStarted>(Error.Validation("Stripe is not set up on this server."));

        var userId = currentUser.RequireId();
        var screening = await db.Screenings.FirstOrDefaultAsync(s => s.Id == command.ScreeningId, ct);
        if (screening is null) return Result.Failure<StripeCheckoutStarted>(Error.NotFound("Screening"));
        if (screening.StartsAtUtc <= DateTime.UtcNow || screening.IsCancelled)
            return Result.Failure<StripeCheckoutStarted>(Error.Conflict("This screening is no longer on sale."));

        await CheckoutHandler.ReleaseExpiredHoldsAsync(db, screening.Id, ct);

        foreach (var seat in command.Seats)
            if (seat.Row < 1 || seat.Row > screening.Rows || seat.Number < 1 || seat.Number > screening.SeatsPerRow)
                return Result.Failure<StripeCheckoutStarted>(Error.Validation($"Seat {seat.Row}-{seat.Number} is not in this hall."));

        var contact = await users.GetContactAsync(userId, ct);
        if (contact is null) return Result.Failure<StripeCheckoutStarted>(Error.Validation("Your account has no e-mail address."));

        var (price, priceError) = await pricing.PriceAsync(screening, command.Seats.Count, command.PromoCode, command.UsePoints, userId, ct);
        if (priceError is not null) return Result.Failure<StripeCheckoutStarted>(priceError);
        if (price.Total < MinimumAmount)
            return Result.Failure<StripeCheckoutStarted>(Error.Validation(
                $"Stripe needs at least {MinimumAmount:0.00} to take a payment. Use the card checkout for this booking."));

        var payment = new SeatPayment
        {
            ScreeningId = screening.Id,
            UserId = userId,
            Reference = CheckoutHandler.NewReference(),
            Amount = price.Total,
            Subtotal = price.Subtotal,
            PromoCode = price.PromoDiscount > 0 ? PromoCode.Normalise(command.PromoCode) : null,
            PromoDiscount = price.PromoDiscount,
            PointsRedeemed = price.PointsUsed,
            PointsDiscount = price.PointsDiscount,
            Provider = PaymentProvider.Stripe,
            Brand = CardBrand.Unknown,
            Last4 = "",
            CardHolder = contact.FullName.Length > 120 ? contact.FullName[..120] : contact.FullName,
            // No code is e-mailed for Stripe; these only satisfy the columns.
            CodeHash = "-",
            Salt = "-",
            // Stripe's session closes at 30 minutes; the seats are held 15 minutes longer, so
            // someone who paid at the last moment still has time to be sent back and confirmed.
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(45),
            Seats = [.. command.Seats.Select(seat => new SeatBooking
            {
                ScreeningId = screening.Id, UserId = userId, Row = seat.Row, Number = seat.Number,
                PricePaid = screening.SeatPrice
            })]
        };

        db.SeatPayments.Add(payment);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result.Failure<StripeCheckoutStarted>(
                Error.Conflict("One of those seats was just taken. Reload the map and try again."));
        }

        var baseUrl = command.ReturnBaseUrl.TrimEnd('/');
        StripeSession session;
        try
        {
            session = await stripe.CreateCheckoutAsync(new StripeCheckoutRequest(
                payment.Id, payment.Reference,
                $"{screening.MovieTitle} · {command.Seats.Count} seat(s) · {payment.Reference}",
                payment.Amount,
                $"{baseUrl}/cinema?stripe=success&payment={payment.Id}&session_id={{CHECKOUT_SESSION_ID}}",
                $"{baseUrl}/cinema?stripe=cancel&payment={payment.Id}",
                contact.Email,
                DateTime.UtcNow.AddMinutes(30)), ct);
        }
        catch (Exception ex) when (ex is StripeException or HttpRequestException or TaskCanceledException)
        {
            // Stripe said no or could not be reached: give the seats straight back.
            db.SeatBookings.RemoveRange(payment.Seats);
            payment.Status = PaymentStatus.Cancelled;
            await db.SaveChangesAsync(CancellationToken.None);
            return Result.Failure<StripeCheckoutStarted>(Error.Conflict($"Stripe could not start the payment: {ex.Message}"));
        }

        payment.ExternalSessionId = session.Id;
        await db.SaveChangesAsync(ct);

        return Result.Success(new StripeCheckoutStarted(payment.Id, payment.Reference, payment.Amount, session.Url, payment.ExpiresAtUtc));
    }
}

public sealed record StripeConfirmCommand(Guid PaymentId, string? SessionId) : ICommand<Result<TicketResponse>>;

internal sealed class StripeConfirmHandler(
    CinemaDbContext db, ICurrentUser currentUser, IStripeGateway stripe, BookingFinalizer finalizer)
    : ICommandHandler<StripeConfirmCommand, Result<TicketResponse>>
{
    public async Task<Result<TicketResponse>> Handle(StripeConfirmCommand command, CancellationToken ct)
    {
        var payment = await db.SeatPayments
            .Include(p => p.Seats)
            .Include(p => p.Screening!).ThenInclude(s => s.HallRoom!).ThenInclude(h => h.Venue)
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, ct);

        if (payment is null) return Result.Failure<TicketResponse>(Error.NotFound("Booking"));
        if (payment.UserId != currentUser.RequireId())
            return Result.Failure<TicketResponse>(Error.Forbidden("This booking belongs to someone else."));
        if (payment.Provider != PaymentProvider.Stripe)
            return Result.Failure<TicketResponse>(Error.Validation("This booking was not paid through Stripe."));

        // Coming back twice (a reload of the success page) is harmless.
        if (payment.Status == PaymentStatus.Confirmed) return Result.Success(TicketMapper.ToTicket(payment));

        if (payment.Status != PaymentStatus.AwaitingCode)
            return Result.Failure<TicketResponse>(Error.Conflict("This booking expired before the payment arrived. Nothing was charged for the seats — contact the help desk if Stripe shows a charge."));

        // The id in the URL has to be the one this booking created. Anything else is either a
        // mistake or someone replaying another booking's success link. An empty id means
        // "check the session you already know about" — the Check payment button on a pending
        // booking, for someone who paid and then lost the return page.
        if (string.IsNullOrEmpty(payment.ExternalSessionId))
            return Result.Failure<TicketResponse>(Error.Conflict("This booking never reached Stripe."));
        if (!string.IsNullOrEmpty(command.SessionId) &&
            !string.Equals(payment.ExternalSessionId, command.SessionId, StringComparison.Ordinal))
            return Result.Failure<TicketResponse>(Error.Validation("That payment session does not belong to this booking."));

        StripeSessionStatus status;
        try
        {
            status = await stripe.GetSessionAsync(payment.ExternalSessionId, ct);
        }
        catch (Exception ex) when (ex is StripeException or HttpRequestException or TaskCanceledException)
        {
            return Result.Failure<TicketResponse>(Error.Conflict($"Could not check the payment with Stripe: {ex.Message}"));
        }

        if (!status.Paid)
            return Result.Failure<TicketResponse>(Error.Validation("Stripe has not received the payment yet."));

        if (status.ClientReferenceId != payment.Id.ToString() || status.AmountTotal != stripe.ToMinorUnits(payment.Amount))
            return Result.Failure<TicketResponse>(Error.Conflict("The Stripe payment does not match this booking."));

        payment.ExternalPaymentId = status.PaymentIntentId;
        return Result.Success(await finalizer.ConfirmAsync(payment, ct));
    }
}

// ------------------------------------------------------------------ PDF ticket

public sealed record GetTicketPdfQuery(Guid PaymentId) : IQuery<(string FileName, byte[] Pdf)?>;

internal sealed class GetTicketPdfHandler(CinemaDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetTicketPdfQuery, (string FileName, byte[] Pdf)?>
{
    public async Task<(string FileName, byte[] Pdf)?> Handle(GetTicketPdfQuery query, CancellationToken ct)
    {
        var payment = await db.SeatPayments.AsNoTracking()
            .Include(p => p.Seats)
            .Include(p => p.Screening!).ThenInclude(s => s.HallRoom!).ThenInclude(h => h.Venue)
            .FirstOrDefaultAsync(p => p.Id == query.PaymentId, ct);

        // Someone else's booking answers exactly like a missing one.
        if (payment is null || payment.UserId != currentUser.RequireId() || payment.Status != PaymentStatus.Confirmed)
            return null;

        var ticket = TicketMapper.ToTicket(payment);
        return ($"WatchingYou-{payment.Reference}.pdf", TicketPdf.Render(ticket, BookingFinalizer.VenueOf(payment)));
    }
}

// ------------------------------------------------------------------ loyalty

public sealed record LoyaltyLine(int Points, string Reason, string? Reference, DateTime AtUtc);

public sealed record LoyaltySummary(
    int Balance, int Held, int Spendable, decimal ManatPerPoint, decimal PointsPerManat, decimal MaxShare,
    IReadOnlyList<LoyaltyLine> History);

public sealed record GetLoyaltyQuery : IQuery<LoyaltySummary>;

internal sealed class GetLoyaltyHandler(CinemaDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetLoyaltyQuery, LoyaltySummary>
{
    public async Task<LoyaltySummary> Handle(GetLoyaltyQuery query, CancellationToken ct)
    {
        var userId = currentUser.RequireId();
        var balance = await LoyaltyLedger.BalanceAsync(db, userId, ct);
        var held = await LoyaltyLedger.HeldAsync(db, userId, ct);

        var history = await db.LoyaltyEntries.AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(30)
            .Select(e => new LoyaltyLine(e.Points, e.Reason.ToString(), e.Reference, e.CreatedAtUtc))
            .ToListAsync(ct);

        return new LoyaltySummary(balance, held, Math.Max(0, balance - held),
            Pricing.ManatPerPoint, Pricing.PointsPerManat, Pricing.MaxPointsShare, history);
    }
}

// ------------------------------------------------------------------ promo codes (admin)

public sealed record PromoCodeRow(
    Guid Id, string Code, string? Description, decimal? PercentOff, decimal? AmountOff, decimal MinSubtotal,
    DateTime? ValidFromUtc, DateTime? ValidUntilUtc, int? MaxRedemptions, int Redemptions, bool IsActive,
    DateTime CreatedAtUtc);

public sealed record GetPromoCodesQuery : IQuery<IReadOnlyList<PromoCodeRow>>;

internal sealed class GetPromoCodesHandler(CinemaDbContext db) : IQueryHandler<GetPromoCodesQuery, IReadOnlyList<PromoCodeRow>>
{
    public async Task<IReadOnlyList<PromoCodeRow>> Handle(GetPromoCodesQuery query, CancellationToken ct) =>
        await db.PromoCodes.AsNoTracking()
            .OrderByDescending(p => p.IsActive).ThenByDescending(p => p.CreatedAtUtc)
            .Select(p => new PromoCodeRow(p.Id, p.Code, p.Description, p.PercentOff, p.AmountOff, p.MinSubtotal,
                p.ValidFromUtc, p.ValidUntilUtc, p.MaxRedemptions, p.Redemptions, p.IsActive, p.CreatedAtUtc))
            .ToListAsync(ct);
}

public sealed record CreatePromoCodeCommand(
    string Code, string? Description, decimal? PercentOff, decimal? AmountOff, decimal MinSubtotal,
    DateTime? ValidFromUtc, DateTime? ValidUntilUtc, int? MaxRedemptions) : ICommand<Result<PromoCodeRow>>;

internal sealed class CreatePromoCodeValidator : AbstractValidator<CreatePromoCodeCommand>
{
    public CreatePromoCodeValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("Use letters, digits, - and _ only.");
        RuleFor(x => x.Description).MaximumLength(200);
        RuleFor(x => x).Must(x => (x.PercentOff is null) != (x.AmountOff is null))
            .WithMessage("Set either a percentage or an amount, not both.");
        RuleFor(x => x.PercentOff).InclusiveBetween(1m, 100m).When(x => x.PercentOff is not null);
        RuleFor(x => x.AmountOff).GreaterThan(0m).When(x => x.AmountOff is not null);
        RuleFor(x => x.MinSubtotal).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.MaxRedemptions).GreaterThan(0).When(x => x.MaxRedemptions is not null);
        RuleFor(x => x).Must(x => x.ValidFromUtc is null || x.ValidUntilUtc is null || x.ValidFromUtc < x.ValidUntilUtc)
            .WithMessage("The end date must be after the start date.");
    }
}

internal sealed class CreatePromoCodeHandler(CinemaDbContext db, IAuditLog audit)
    : ICommandHandler<CreatePromoCodeCommand, Result<PromoCodeRow>>
{
    public async Task<Result<PromoCodeRow>> Handle(CreatePromoCodeCommand command, CancellationToken ct)
    {
        var code = PromoCode.Normalise(command.Code);
        if (await db.PromoCodes.AnyAsync(p => p.Code == code, ct))
            return Result.Failure<PromoCodeRow>(Error.Conflict($"{code} already exists."));

        var promo = new PromoCode
        {
            Code = code,
            Description = command.Description?.Trim(),
            PercentOff = command.PercentOff,
            AmountOff = command.AmountOff,
            MinSubtotal = command.MinSubtotal,
            ValidFromUtc = command.ValidFromUtc,
            ValidUntilUtc = command.ValidUntilUtc,
            MaxRedemptions = command.MaxRedemptions
        };
        db.PromoCodes.Add(promo);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry("promo.created", code,
            promo.PercentOff is { } p ? $"{p:0.##}% off" : $"{promo.AmountOff:0.00} off", promo.Id), ct);

        return Result.Success(new PromoCodeRow(promo.Id, promo.Code, promo.Description, promo.PercentOff, promo.AmountOff,
            promo.MinSubtotal, promo.ValidFromUtc, promo.ValidUntilUtc, promo.MaxRedemptions, promo.Redemptions,
            promo.IsActive, promo.CreatedAtUtc));
    }
}

public sealed record SetPromoActiveCommand(Guid Id, bool Active) : ICommand<Result>;

internal sealed class SetPromoActiveHandler(CinemaDbContext db, IAuditLog audit) : ICommandHandler<SetPromoActiveCommand, Result>
{
    public async Task<Result> Handle(SetPromoActiveCommand command, CancellationToken ct)
    {
        var promo = await db.PromoCodes.FirstOrDefaultAsync(p => p.Id == command.Id, ct);
        if (promo is null) return Result.Failure(Error.NotFound("Promo code"));

        promo.IsActive = command.Active;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(command.Active ? "promo.enabled" : "promo.disabled", promo.Code, null, promo.Id), ct);
        return Result.Success();
    }
}

// ------------------------------------------------------------------ endpoints

public sealed record PaymentOptions(bool Card, bool Stripe, decimal ManatPerPoint, decimal MaxPointsShare);

public static class PaymentEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/payments/options", (IStripeGateway stripe) =>
                Results.Ok(new PaymentOptions(true, stripe.Enabled, Pricing.ManatPerPoint, Pricing.MaxPointsShare)))
            .WithName("GetPaymentOptions").WithTags("Payments").AllowAnonymous();

        app.MapPost("/api/screenings/{id:guid}/quote", async (
                Guid id, QuoteRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var quote = await dispatcher.Ask(new GetCheckoutQuoteQuery(id, body), ct);
                return quote is null ? Results.NotFound() : Results.Ok(quote);
            })
            .WithName("GetCheckoutQuote").WithTags("Payments").RequireAuthorization();

        app.MapPost("/api/screenings/{id:guid}/checkout/stripe",
            async Task<Results<Ok<StripeCheckoutStarted>, BadRequest<Error>, Conflict<Error>, NotFound<Error>>> (
                Guid id, StripeCheckoutCommand body, HttpRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            {
                // Stripe sends the browser back to whichever host it came from, so the same
                // build works on localhost and on a real domain without configuration.
                var baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}";
                var result = await dispatcher.Send(body with { ScreeningId = id, ReturnBaseUrl = baseUrl }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "conflict" => TypedResults.Conflict(result.Error),
                    _ => TypedResults.BadRequest(result.Error)
                };
            })
            .WithName("StartStripeCheckout").WithTags("Payments").RequireAuthorization();

        app.MapPost("/api/bookings/{paymentId:guid}/stripe/confirm",
            async Task<Results<Ok<TicketResponse>, BadRequest<Error>, Conflict<Error>, NotFound<Error>, ForbidHttpResult>> (
                Guid paymentId, StripeConfirmCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { PaymentId = paymentId }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "forbidden" => TypedResults.Forbid(),
                    "conflict" => TypedResults.Conflict(result.Error),
                    _ => TypedResults.BadRequest(result.Error)
                };
            })
            .WithName("ConfirmStripeBooking").WithTags("Payments").RequireAuthorization();

        app.MapGet("/api/bookings/{paymentId:guid}/ticket.pdf", async (
                Guid paymentId, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var file = await dispatcher.Ask(new GetTicketPdfQuery(paymentId), ct);
                return file is { } f ? Results.File(f.Pdf, "application/pdf", f.FileName) : Results.NotFound();
            })
            .WithName("DownloadTicketPdf").WithTags("Cinema").RequireAuthorization();

        app.MapGet("/api/loyalty", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetLoyaltyQuery(), ct)))
            .WithName("GetLoyalty").WithTags("Payments").RequireAuthorization();

        var admin = app.MapGroup("/api/admin/promos").WithTags("Payments").RequireAuthorization(AppRoles.Admin);

        admin.MapGet("", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetPromoCodesQuery(), ct)))
            .WithName("GetPromoCodes");

        admin.MapPost("", async Task<Results<Ok<PromoCodeRow>, Conflict<Error>>> (
                CreatePromoCodeCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body, ct);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.Conflict(result.Error);
            })
            .WithName("CreatePromoCode");

        admin.MapPut("/{id:guid}/active", async Task<Results<NoContent, NotFound<Error>>> (
                Guid id, SetPromoActiveCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { Id = id }, ct);
                return result.IsSuccess ? TypedResults.NoContent() : TypedResults.NotFound(result.Error);
            })
            .WithName("SetPromoActive");
    }
}
