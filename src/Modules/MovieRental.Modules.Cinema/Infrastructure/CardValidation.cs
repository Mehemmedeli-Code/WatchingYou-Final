using MovieRental.Modules.Cinema.Domain;
using MovieRental.SharedKernel.Payments;

namespace MovieRental.Modules.Cinema.Infrastructure;

/// <summary>The shared card rules (<see cref="CardRules"/>), with the brand as this module's enum.</summary>
internal static class CardValidation
{
    public static string Digits(string? value) => CardRules.Digits(value);

    public static bool PassesLuhn(string digits) => CardRules.PassesLuhn(digits);

    public static CardBrand BrandOf(string digits) => CardRules.BrandName(digits) switch
    {
        "Visa" => CardBrand.Visa,
        "Mastercard" => CardBrand.Mastercard,
        _ => CardBrand.Unknown
    };

    public static bool ExpiryIsFuture(int month, int year) => CardRules.ExpiryIsFuture(month, year);

    public static bool CvcLooksRight(string cvc) => CardRules.CvcLooksRight(cvc);
}
