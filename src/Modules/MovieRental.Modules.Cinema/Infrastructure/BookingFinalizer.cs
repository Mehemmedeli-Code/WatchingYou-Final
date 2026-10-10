using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Features;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Contracts;

namespace MovieRental.Modules.Cinema.Infrastructure;

/// <summary>
/// Everything that happens once a booking is paid for, whichever way it was paid: the seats
/// become real, points move, the promo code is counted, and the ticket goes out by e-mail as a
/// PDF. Both the code-confirmed card checkout and the Stripe return trip end here, so they
/// cannot drift apart.
/// </summary>
internal sealed class BookingFinalizer(
    CinemaDbContext db, IUserDirectory users, IEmailSender email, ILogger<BookingFinalizer> logger)
{
    /// <summary>The payment must be tracked and loaded with its seats and screening. Returns
    /// null when the hold expired (or was released) before this confirm could claim it.</summary>
    public async Task<TicketResponse?> ConfirmAsync(SeatPayment payment, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Claim the payment in one conditional UPDATE. Two confirms at once (a double tap, the
        // Stripe return racing "Check payment") or a confirm racing the expiry sweep all read
        // AwaitingCode; only the one whose UPDATE hits a row goes on to award points, count the
        // promo and send the ticket.
        // One transaction with the writes below, so a failed save never leaves a "Confirmed"
        // payment without its seats confirmed or its points moved.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var claimed = await db.SeatPayments.IgnoreQueryFilters()
            .Where(p => p.Id == payment.Id && p.Status == PaymentStatus.AwaitingCode)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PaymentStatus.Confirmed)
                .SetProperty(p => p.ConfirmedAtUtc, now), ct);
        if (claimed == 0)
        {
            await db.Entry(payment).ReloadAsync(ct);
            return payment.Status == PaymentStatus.Confirmed ? TicketMapper.ToTicket(payment) : null;
        }

        payment.Status = PaymentStatus.Confirmed;
        payment.ConfirmedAtUtc = now;
        foreach (var seat in payment.Seats) seat.ConfirmedAtUtc = now;

        if (payment.PointsRedeemed > 0)
        {
            db.LoyaltyEntries.Add(new LoyaltyEntry
            {
                UserId = payment.UserId, Points = -payment.PointsRedeemed, Reason = LoyaltyReason.Redeemed,
                PaymentId = payment.Id, Reference = payment.Reference
            });
        }

        payment.PointsEarned = (int)Math.Floor(payment.Amount * Pricing.PointsPerManat);
        if (payment.PointsEarned > 0)
        {
            db.LoyaltyEntries.Add(new LoyaltyEntry
            {
                UserId = payment.UserId, Points = payment.PointsEarned, Reason = LoyaltyReason.Earned,
                PaymentId = payment.Id, Reference = payment.Reference
            });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (!string.IsNullOrEmpty(payment.PromoCode))
        {
            // Conditional, so two bookings racing for the last use of a capped code cannot
            // push the counter past its limit. Losing that race costs the shop one discount,
            // which is a better failure than refusing a customer who has already paid.
            await db.PromoCodes
                .Where(p => p.Code == payment.PromoCode && (p.MaxRedemptions == null || p.Redemptions < p.MaxRedemptions))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Redemptions, p => p.Redemptions + 1), ct);
        }

        var ticket = TicketMapper.ToTicket(payment);
        await SendTicketAsync(payment, ticket, ct);
        return ticket;
    }

    /// <summary>Puts points back the way they were: spent points return, earned points go.
    /// Earned points already spent elsewhere are only taken back as far as the balance allows —
    /// a refund should not leave anyone owing points.</summary>
    public async Task ReverseLoyaltyAsync(SeatPayment payment, CancellationToken ct)
    {
        var balance = await LoyaltyLedger.BalanceAsync(db, payment.UserId, ct);

        if (payment.PointsRedeemed > 0)
        {
            db.LoyaltyEntries.Add(new LoyaltyEntry
            {
                UserId = payment.UserId, Points = payment.PointsRedeemed, Reason = LoyaltyReason.Restored,
                PaymentId = payment.Id, Reference = payment.Reference
            });
            balance += payment.PointsRedeemed;
        }

        var takeBack = Math.Min(payment.PointsEarned, Math.Max(0, balance));
        if (takeBack > 0)
        {
            db.LoyaltyEntries.Add(new LoyaltyEntry
            {
                UserId = payment.UserId, Points = -takeBack, Reason = LoyaltyReason.Reversed,
                PaymentId = payment.Id, Reference = payment.Reference
            });
        }
    }

    public static string? VenueOf(SeatPayment payment) => payment.Screening?.HallRoom?.Venue?.Name;

    private async Task SendTicketAsync(SeatPayment payment, TicketResponse ticket, CancellationToken ct)
    {
        try
        {
            var contact = await users.GetContactAsync(payment.UserId, ct);
            if (contact is null) return;

            var pdf = TicketPdf.Render(ticket, VenueOf(payment));
            var seats = string.Join(", ", ticket.Seats.Select(s => s.Label));
            var discounts = payment.PromoDiscount + payment.PointsDiscount > 0
                ? $"<p>Saved {payment.PromoDiscount + payment.PointsDiscount:0.00}" +
                  (payment.PromoCode is null ? "" : $" with {payment.PromoCode}") +
                  (payment.PointsRedeemed > 0 ? $" and {payment.PointsRedeemed} points" : "") + ".</p>"
                : "";

            await email.SendAsync(new EmailRequest(contact.Email,
                $"Your tickets — {ticket.MovieTitle} · {ticket.Reference}",
                $"""
                 <p>Hi {System.Net.WebUtility.HtmlEncode(contact.FullName)},</p>
                 <p>You are booked for <strong>{ticket.MovieTitle}</strong>, hall {ticket.Hall}, seats {seats}.</p>
                 <p>Your tickets are attached as a PDF — one page per seat, each with its own QR code to show at the door.</p>
                 <p>Paid {ticket.Amount:0.00}. {(payment.PointsEarned > 0 ? $"You earned {payment.PointsEarned} loyalty points." : "")}</p>
                 {discounts}
                 <p>Reference {ticket.Reference}.</p>
                 """)
            {
                Attachments = [new EmailAttachment($"WatchingYou-{ticket.Reference}.pdf", "application/pdf", pdf)]
            }, ct);
        }
        catch (Exception ex)
        {
            // The booking is confirmed and paid; a mail server hiccup must not undo that. The
            // ticket is still on the site, and the PDF can be downloaded from there.
            logger.LogWarning(ex, "Ticket e-mail for {Reference} could not be sent.", payment.Reference);
        }
    }
}

internal static class LoyaltyLedger
{
    public static async Task<int> BalanceAsync(CinemaDbContext db, Guid userId, CancellationToken ct) =>
        await db.LoyaltyEntries.Where(e => e.UserId == userId).SumAsync(e => e.Points, ct);

    /// <summary>Points already promised to checkouts that are still waiting for a code or for
    /// Stripe. Counting them stops the same points being spent twice in two tabs.</summary>
    public static async Task<int> HeldAsync(CinemaDbContext db, Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await db.SeatPayments
            .Where(p => p.UserId == userId && p.Status == PaymentStatus.AwaitingCode && p.ExpiresAtUtc > now)
            .SumAsync(p => p.PointsRedeemed, ct);
    }

    public static async Task<int> SpendableAsync(CinemaDbContext db, Guid userId, CancellationToken ct) =>
        Math.Max(0, await BalanceAsync(db, userId, ct) - await HeldAsync(db, userId, ct));
}
