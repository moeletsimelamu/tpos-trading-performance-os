using System.ComponentModel.DataAnnotations;

namespace MelamuHoldings.Application.Finance;

// An optional amount that, when given, must be positive. Limits are parsed invariantly: RangeAttribute's default
// uses the server's culture, which breaks on machines whose decimal separator is a comma.
[AttributeUsage(AttributeTargets.Property)]
public sealed class PositiveAmountAttribute : RangeAttribute
{
    public PositiveAmountAttribute() : base(typeof(decimal), "0.01", "999999999999")
    {
        ParseLimitsInInvariantCulture = true;
        ConvertValueInInvariantCulture = true;
        ErrorMessage = "{0} must be greater than zero.";
    }
}
