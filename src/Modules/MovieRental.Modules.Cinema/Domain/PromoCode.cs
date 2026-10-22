using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Cinema.Domain;

/// <summary>
/// A discount code the admin hands out: a percentage or a fixed amount off a cinema booking,
/// optionally with a minimum spend, a date window and a cap on how many times it can be used.
///
/// Codes are stored upper-case and compared that way, so "autumn10" and "AUTUMN10" are one code.
/// </summary>
public sealed class PromoCode : BaseEntity
{
    public required string Code { get; set; }
    public string? Description { get; set; }

    /// <summary>0–100. Exactly one of PercentOff and AmountOff is set.</summary>
    public decimal? PercentOff { get; set; }
    public decimal? AmountOff { get; set; }

    public decimal MinSubtotal { get; set; }
    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ValidUntilUtc { get; set; }

    /// <summary>Null for unlimited.</summary>
    public int? MaxRedemptions { get; set; }
    public int Redemptions { get; set; }

    public bool IsActive { get; set; } = true;

    public static string Normalise(string? code) => (code ?? "").Trim().ToUpperInvariant();
}

public enum PromoRejection
{
    None = 0,
    Unknown,
    Inactive,
    NotYetValid,
    Expired,
    UsedUp,
    BelowMinimum
}

public sealed record PromoEvaluation(bool Applies, decimal Discount, PromoRejection Rejection)
{
    public static PromoEvaluation Rejected(PromoRejection why) => new(false, 0, why);
}

/// <summary>
/// Everything a cinema booking costs, worked out in one place: the seats, then the promo code,
/// then loyalty points. The checkout, the price preview in the browser and the tests all call
/// this, so the number shown before paying is the number charged.
/// </summary>
public static class Pricing
{
    /// <summary>One point per whole manat actually paid.</summary>
    public const decimal PointsPerManat = 1m;

    /// <summary>What a point is worth when spent: twenty points take one manat off.</summary>
    public const decimal ManatPerPoint = 0.05m;

    /// <summary>Points may pay for at most half of what is left after the promo code, so a
    /// booking always involves some real money and cannot be farmed for more points.</summary>
    public const decimal MaxPointsShare = 0.5m;

    // Money rounds half away from zero, the way a till does. .NET's default (to even) would
    // make 3.825 come out as 3.82.
    public static PromoEvaluation Evaluate(PromoCode? promo, decimal subtotal, DateTime nowUtc)
    {
        if (promo is null) return PromoEvaluation.Rejected(PromoRejection.Unknown);
        if (!promo.IsActive) return PromoEvaluation.Rejected(PromoRejection.Inactive);
        if (promo.ValidFromUtc is { } from && nowUtc < from) return PromoEvaluation.Rejected(PromoRejection.NotYetValid);
        if (promo.ValidUntilUtc is { } until && nowUtc > until) return PromoEvaluation.Rejected(PromoRejection.Expired);
        if (promo.MaxRedemptions is { } max && promo.Redemptions >= max) return PromoEvaluation.Rejected(PromoRejection.UsedUp);
        if (subtotal < promo.MinSubtotal) return PromoEvaluation.Rejected(PromoRejection.BelowMinimum);

        var discount = promo.PercentOff is { } percent
            ? Math.Round(subtotal * Math.Clamp(percent, 0m, 100m) / 100m, 2, MidpointRounding.AwayFromZero)
            : Math.Max(0m, promo.AmountOff ?? 0m);

        return new PromoEvaluation(true, Math.Min(discount, subtotal), PromoRejection.None);
    }

    public static PriceBreakdown Calculate(
        decimal seatPrice, int seats, PromoCode? promo, DateTime nowUtc, int pointsBalance, bool usePoints)
    {
        var subtotal = Math.Round(seatPrice * seats, 2, MidpointRounding.AwayFromZero);

        var promoEval = promo is null ? null : Evaluate(promo, subtotal, nowUtc);
        var promoDiscount = promoEval?.Applies == true ? promoEval.Discount : 0m;
        var afterPromo = subtotal - promoDiscount;

        var pointsUsed = 0;
        var pointsDiscount = 0m;
        if (usePoints && pointsBalance > 0 && afterPromo > 0)
        {
            var cap = Math.Floor(afterPromo * MaxPointsShare / ManatPerPoint);   // points that fit under the cap
            pointsUsed = (int)Math.Min(pointsBalance, cap);
            pointsDiscount = Math.Round(pointsUsed * ManatPerPoint, 2, MidpointRounding.AwayFromZero);
        }

        var total = Math.Max(0m, afterPromo - pointsDiscount);
        var earns = (int)Math.Floor(total * PointsPerManat);

        return new PriceBreakdown(
            subtotal, promoDiscount, promoEval?.Rejection ?? PromoRejection.None,
            pointsUsed, pointsDiscount, total, earns);
    }

    public static string Explain(PromoRejection rejection) => rejection switch
    {
        PromoRejection.Unknown => "That promo code does not exist.",
        PromoRejection.Inactive => "That promo code has been switched off.",
        PromoRejection.NotYetValid => "That promo code is not valid yet.",
        PromoRejection.Expired => "That promo code has expired.",
        PromoRejection.UsedUp => "That promo code has been used up.",
        PromoRejection.BelowMinimum => "The booking is below this code's minimum spend.",
        _ => ""
    };
}

public sealed record PriceBreakdown(
    decimal Subtotal, decimal PromoDiscount, PromoRejection PromoRejection,
    int PointsUsed, decimal PointsDiscount, decimal Total, int PointsEarned);
