using System.Reflection;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.UnitTests.Finance;

// Phase 7.2: the continuity settings (PLAN.md D6–D8)
public class ContinuitySettingsTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Fund Continuity = WithId(new Fund("Business Continuity", null, Now), 3);
    private static readonly Fund Restoration = WithId(new Fund("Trading Restoration", null, Now), 6);

    [Fact]
    public void The_target_is_months_of_the_entered_expense_in_rand_and_none_until_it_is_entered()
    {
        var settings = new ContinuitySettings(Continuity.Id);
        Assert.Equal((ContinuitySettings.DefaultMonths, Currency.ZAR), (settings.Months, settings.Currency));
        Assert.Null(settings.Target);

        settings.Update(Continuity, 18, 12500.50m, Restoration, Now);
        Assert.Equal(225009m, settings.Target);
        Assert.Equal((Restoration.Id, Now), (settings.RestorationFundId, settings.UpdatedAt));

        settings.Update(Continuity, 18, null, null, Now);
        Assert.Null(settings.Target);
        Assert.Null(settings.RestorationFundId);
    }

    [Fact]
    public void Continuity_and_restoration_are_different_funds_and_neither_is_the_system_fund()
    {
        var settings = new ContinuitySettings(Continuity.Id);
        var unallocated = WithId(new Fund("Unallocated", null, Now), 1);
        typeof(Fund).GetProperty(nameof(Fund.IsSystem))!.SetValue(unallocated, true);

        Assert.Throws<DomainException>(() => settings.Update(Continuity, 24, null, Continuity, Now));
        Assert.Throws<DomainException>(() => settings.Update(unallocated, 24, null, null, Now));
        Assert.Throws<DomainException>(() => settings.Update(Continuity, 24, null, unallocated, Now));
        Assert.Throws<DomainException>(() => settings.Update(Continuity, 0, null, null, Now));
        Assert.Throws<DomainException>(() => settings.Update(Continuity, ContinuitySettings.MaxMonths + 1, null, null, Now));
        Assert.Throws<DomainException>(() => settings.Update(Continuity, 24, 0, null, Now));
        Assert.Throws<DomainException>(() => settings.Update(Continuity, 24, 10.005m, null, Now));

        // Nothing refused was kept
        Assert.Equal((24, (decimal?)null, (int?)null, (DateTime?)null), (settings.Months, settings.MonthlyEssentialExpense, settings.RestorationFundId, settings.UpdatedAt));
    }

    private static T WithId<T>(T entity, int id)
    {
        typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);
        return entity;
    }
}
