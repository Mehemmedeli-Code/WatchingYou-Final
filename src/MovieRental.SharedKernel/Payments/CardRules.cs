namespace MovieRental.SharedKernel.Payments;

/// <summary>
/// Card checks that can be done without an acquirer: the Luhn checksum, the brand prefix,
/// an expiry in the future and a CVC of the right length. These catch typos, which is what
/// most failed payments actually are. They prove nothing about funds.
/// One copy for the whole site: the seat checkout, rentals and Watching PRO all use it.
/// </summary>
public static class CardRules
{
    public static string Digits(string? value) =>
        new(value?.Where(char.IsDigit).ToArray() ?? []);

    /// <summary>Luhn: double every second digit from the right, subtract 9 when that goes
    /// above nine, and the total must divide by ten.</summary>
    public static bool PassesLuhn(string digits)
    {
        if (digits.Length is < 12 or > 19) return false;

        var sum = 0;
        var doubling = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var digit = digits[i] - '0';
            if (doubling && (digit *= 2) > 9) digit -= 9;
            sum += digit;
            doubling = !doubling;
        }
        return sum % 10 == 0;
    }

    /// <summary>"Visa", "Mastercard", or null for anything else.</summary>
    public static string? BrandName(string digits)
    {
        if (digits.StartsWith('4')) return "Visa";
        if (digits.Length >= 2 && int.TryParse(digits[..2], out var two) && two is >= 51 and <= 55) return "Mastercard";
        // Mastercard's 2-series, added in 2017 and still missed by a lot of naive checks.
        if (digits.Length >= 4 && int.TryParse(digits[..4], out var four) && four is >= 2221 and <= 2720) return "Mastercard";
        return null;
    }

    public static bool ExpiryIsFuture(int month, int year)
    {
        if (month is < 1 or > 12) return false;
        if (year < 100) year += 2000;
        if (year is < 2000 or > 2100) return false;

        var lastDay = new DateTime(year, month, DateTime.DaysInMonth(year, month), 23, 59, 59, DateTimeKind.Utc);
        return lastDay >= DateTime.UtcNow;
    }

    /// <summary>Three digits. Four belongs to American Express, which is refused anyway, so
    /// accepting it would only let a typo through.</summary>
    public static bool CvcLooksRight(string? cvc) => cvc is { Length: 3 } && cvc.All(char.IsDigit);
}
