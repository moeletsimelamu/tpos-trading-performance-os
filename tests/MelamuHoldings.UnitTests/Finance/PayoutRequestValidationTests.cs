using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MelamuHoldings.Application.Finance;

namespace MelamuHoldings.UnitTests.Finance;

public class PayoutRequestValidationTests
{
    // Regression: RangeAttribute parsed "0.01" with the server culture and threw on comma-decimal machines
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("af-ZA")]
    public void An_optional_fee_is_validated_the_same_in_every_culture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.True(IsValid(Receive(fee: 0.5m)));
            Assert.True(IsValid(Receive(fee: null)));
            Assert.False(IsValid(Receive(fee: 0m)));
            Assert.False(IsValid(Receive(fee: -1m)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static ReceivePayoutRequest Receive(decimal? fee) =>
        new() { AccountId = 1, AmountCredited = 100m, Fee = fee, Date = new DateOnly(2026, 10, 27) };

    private static bool IsValid(object request) =>
        Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), validateAllProperties: true);
}
