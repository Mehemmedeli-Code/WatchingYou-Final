using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Infrastructure;

namespace MovieRental.Tests;

/// <summary>
/// These checks catch typos, which is what most failed payments actually are. They prove
/// nothing about funds — there is no acquirer behind this checkout — so the tests are about
/// the arithmetic and the brand ranges, not about authorisation.
/// </summary>
public class CardValidationTests
{
    [Theory]
    [InlineData("4242424242424242")]   // the Visa test number everyone knows
    [InlineData("4169738812345670")]   // a Kapital Bank BIN with a valid check digit
    [InlineData("5555555555554444")]   // Mastercard 51–55
    [InlineData("2223003122003222")]   // Mastercard 2-series
    public void Valid_numbers_pass_luhn(string number) =>
        Assert.True(CardValidation.PassesLuhn(number));

    [Theory]
    [InlineData("4242424242424241")]   // last digit altered
    [InlineData("1234567812345678")]
    [InlineData("")]
    [InlineData("42424242")]           // too short to be a card
    public void Invalid_numbers_fail_luhn(string number) =>
        Assert.False(CardValidation.PassesLuhn(number));

    [Fact]
    public void Digits_strips_the_spaces_the_input_adds()
    {
        // The form groups digits in fours as you type; the handler must not see them.
        Assert.Equal("4242424242424242", CardValidation.Digits("4242 4242 4242 4242"));
        Assert.Equal("4242", CardValidation.Digits("4a2b4c2"));
    }

    [Theory]
    [InlineData("4242424242424242", CardBrand.Visa)]
    [InlineData("5105105105105100", CardBrand.Mastercard)]
    [InlineData("2221001234567890", CardBrand.Mastercard)]   // lower edge of the 2-series
    [InlineData("2720991234567890", CardBrand.Mastercard)]   // upper edge
    [InlineData("378282246310005", CardBrand.Unknown)]       // Amex, refused by this checkout
    [InlineData("6011111111111117", CardBrand.Unknown)]      // Discover
    public void Brand_is_read_from_the_prefix(string number, CardBrand expected) =>
        Assert.Equal(expected, CardValidation.BrandOf(number));

    [Fact]
    public void The_mastercard_two_series_is_bounded_on_both_sides()
    {
        // 2220 and 2721 sit just outside the range Mastercard was allocated in 2017. A naive
        // "starts with 2" check would wrongly claim both.
        Assert.Equal(CardBrand.Unknown, CardValidation.BrandOf("2220001234567890"));
        Assert.Equal(CardBrand.Unknown, CardValidation.BrandOf("2721001234567890"));
    }

    [Fact]
    public void An_expiry_this_month_is_still_valid()
    {
        // Cards work until the last day of their month, not until the first.
        var now = DateTime.UtcNow;
        Assert.True(CardValidation.ExpiryIsFuture(now.Month, now.Year));
    }

    [Fact]
    public void A_past_expiry_is_rejected()
    {
        var past = DateTime.UtcNow.AddMonths(-1);
        Assert.False(CardValidation.ExpiryIsFuture(past.Month, past.Year));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void A_month_outside_one_to_twelve_is_rejected(int month) =>
        Assert.False(CardValidation.ExpiryIsFuture(month, DateTime.UtcNow.Year + 2));

    [Fact]
    public void Two_digit_years_are_read_as_this_century() =>
        Assert.True(CardValidation.ExpiryIsFuture(12, DateTime.UtcNow.Year % 100 + 2));

    [Theory]
    [InlineData("123", true)]
    [InlineData("1234", false)]   // four digits is Amex, which is refused anyway
    [InlineData("12", false)]
    [InlineData("12a", false)]
    public void The_security_code_is_three_digits(string cvc, bool expected) =>
        Assert.Equal(expected, CardValidation.CvcLooksRight(cvc));
}
