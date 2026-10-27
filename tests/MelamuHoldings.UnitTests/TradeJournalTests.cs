using System.ComponentModel.DataAnnotations;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.UnitTests;

public class TradeJournalTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static Trade Draft() => new() { Id = 1, UserId = 1, TradeDate = new DateOnly(2026, 10, 26), Instrument = "NQ", Status = TradeStatus.Draft };

    private static Trade Closed(int id, decimal pnl, decimal? rr = 1m, TradeStatus status = TradeStatus.Closed) => new()
    {
        Id = id, UserId = 1, TradeDate = new DateOnly(2026, 10, 26), Instrument = "NQ", Status = status,
        Direction = TradeDirection.Long, EntryTime = new TimeOnly(9, 30), EntryPrice = 100, ExitPrice = 101, PnL = pnl, RR = rr,
        Session = "New York", DayOfWeek = "Monday"
    };

    [Fact]
    public void Each_status_lists_exactly_the_fields_it_still_needs()
    {
        var trade = Draft();

        Assert.Empty(trade.MissingFor(TradeStatus.Draft));
        Assert.Equal(["Direction", "EntryTime", "EntryPrice"], trade.MissingFor(TradeStatus.Open));
        Assert.Equal(["Direction", "EntryTime", "EntryPrice", "ExitPrice", "PnL"], trade.MissingFor(TradeStatus.Closed));
        Assert.Equal(["Instrument"], new Trade { TradeDate = new DateOnly(2026, 10, 26) }.MissingFor(TradeStatus.Draft));
    }

    [Fact]
    public void A_status_is_refused_until_its_fields_are_recorded()
    {
        var trade = Draft();

        var ex = Assert.Throws<DomainException>(() => trade.SetStatus(TradeStatus.Closed));
        Assert.Contains("ExitPrice, PnL", ex.Message);
        Assert.Equal(TradeStatus.Draft, trade.Status);

        trade.Direction = TradeDirection.Short;
        trade.EntryTime = new TimeOnly(9, 31);
        trade.EntryPrice = 20100;
        trade.SetStatus(TradeStatus.Open);
        Assert.Equal(TradeStatus.Open, trade.Status);
    }

    [Fact]
    public void Reviewing_needs_a_closed_trade_and_notes()
    {
        var draft = Draft();
        Assert.Throws<DomainException>(() => draft.MarkReviewed("Followed the plan", Now));

        var closed = Closed(2, 200);
        Assert.Throws<DomainException>(() => closed.MarkReviewed("  ", Now));
        Assert.Throws<DomainException>(() => closed.SetStatus(TradeStatus.Reviewed));

        closed.MarkReviewed("Followed the plan; exit was early.", Now);
        Assert.Equal(TradeStatus.Reviewed, closed.Status);
        Assert.Equal(Now, closed.ReviewedAt);
    }

    [Fact]
    public void Derived_values_come_only_from_recorded_figures()
    {
        var trade = Closed(3, 300, rr: null);
        trade.RiskAmount = 150;
        trade.ExitDate = new DateOnly(2026, 10, 27);
        trade.ExitTime = new TimeOnly(9, 0);

        trade.ApplyDerivedValues();

        Assert.Equal(2m, trade.RR);                     // 300 ÷ 150
        Assert.Equal(23 * 60 + 30, trade.TradeDuration);  // 26 Oct 09:30 → 27 Oct 09:00
        Assert.Equal("Monday", trade.DayOfWeek);

        var noRisk = Closed(4, 300, rr: null);
        noRisk.ApplyDerivedValues();
        Assert.Null(noRisk.RR);                          // no risk recorded: no R, not 0R
    }

    [Fact]
    public void Only_closed_and_reviewed_trades_reach_analytics()
    {
        var open = Closed(6, 50);
        open.Status = TradeStatus.Open;
        var trades = new[] { Draft(), open, Closed(7, 100), Closed(8, -40, status: TradeStatus.Reviewed) };

        Assert.Equal([7, 8], CompletedTrade.From(trades).Select(t => t.Id));
    }

    [Fact]
    public async Task Analytics_ignore_drafts_and_average_r_only_over_trades_that_have_one()
    {
        var draft = Draft();
        draft.PnL = 5000;  // a draft's figures never count, whatever they say
        var service = new AnalyticsService(new InMemoryTradeRepository(draft, Closed(7, 100, rr: 2m), Closed(8, -50, rr: null)));

        var summary = await service.GetSummaryAsync(1);

        Assert.Equal(2, summary.TotalTrades);
        Assert.Equal(50m, summary.TotalPnL);
        Assert.Equal(2m, summary.AverageRR);
    }

    [Theory]
    [InlineData("Reviewed")]
    [InlineData("Pending")]
    public void Status_on_save_is_draft_open_or_closed(string status)
    {
        var dto = new CreateTradeDto { TradeDate = new DateOnly(2026, 10, 26), Instrument = "NQ", Status = status };

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateTradeDto.Status)));
    }

    [Fact]
    public void A_draft_with_only_date_and_instrument_passes_validation()
    {
        var dto = new CreateTradeDto { TradeDate = new DateOnly(2026, 10, 26), Instrument = "NQ", Status = "Draft" };

        Assert.True(Validator.TryValidateObject(dto, new ValidationContext(dto), new List<ValidationResult>(), validateAllProperties: true));
    }
}
