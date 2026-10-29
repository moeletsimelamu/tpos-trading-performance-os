using System.Text.Json;
using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Trading;

public enum ImportMatchStatus { Pending, Linked, ImportedSeparately, Dismissed }

// One CSV row as the import read it
public sealed record ImportedRow(
    int RowNumber, DateOnly TradeDate, TimeOnly EntryTime, string Instrument, TradeDirection Direction,
    decimal EntryPrice, decimal ExitPrice, decimal RiskAmount, decimal PnL, decimal RR, string? Strategy,
    string Session, string DayOfWeek, int TradeDuration, bool IsManualOverride, string? DeviationNotes, string? Notes)
{
    // A trade already imported with the same values: importing it again would only duplicate it
    public bool Duplicates(Trade t) =>
        t.IsImported && t.TradeDate == TradeDate && SameInstrument(t) && t.Direction == Direction
        && t.EntryTime == EntryTime && t.EntryPrice == EntryPrice && t.ExitPrice == ExitPrice && t.PnL == PnL;

    // A trade journaled by hand that is very likely this one: same day and instrument, and no recorded value
    // that disagrees on direction, entry time (to the minute) or entry price. At least one of entry time or
    // entry price must be recorded, so a bare draft is not matched on the day alone.
    public bool Matches(Trade t) =>
        !t.IsImported && t.TradeDate == TradeDate && SameInstrument(t)
        && (t.Direction is null || t.Direction == Direction)
        && (t.EntryTime is null || SameMinute(t.EntryTime.Value, EntryTime))
        && (t.EntryPrice is null || t.EntryPrice == EntryPrice)
        && (t.EntryTime is not null || t.EntryPrice is not null);

    private bool SameInstrument(Trade t) => string.Equals(t.Instrument.Trim(), Instrument.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool SameMinute(TimeOnly a, TimeOnly b) => a.Hour == b.Hour && a.Minute == b.Minute;
}

// A CSV row that matched a trade journaled by hand. Nothing is imported until the trader decides: link the row
// to that trade (filling only what the trade does not have yet), import it as a separate trade, or leave it out.
public class ImportMatch
{
    public int Id { get; private set; }
    public int UserId { get; private set; }
    public int UploadLogId { get; private set; }
    // Null once the matched trade is deleted; the row can then still be imported or left out
    public int? TradeId { get; private set; }
    public int RowNumber { get; private set; }
    public string Row { get; private set; } = "{}";
    public ImportMatchStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public int? CreatedTradeId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public ImportedRow Values => JsonSerializer.Deserialize<ImportedRow>(Row)!;

    private ImportMatch() { }

    public ImportMatch(int userId, int uploadLogId, int tradeId, ImportedRow row, DateTime now)
    {
        UserId = userId;
        UploadLogId = uploadLogId;
        TradeId = tradeId;
        RowNumber = row.RowNumber;
        Row = JsonSerializer.Serialize(row);
        Status = ImportMatchStatus.Pending;
        CreatedAt = now;
    }

    // Copies the row's values into the fields the trade has left empty. Values the trader recorded, the notes
    // and the status are never changed. Returns the names of the fields filled.
    public IReadOnlyList<string> Link(Trade trade, int? strategyId, DateTime now)
    {
        Resolve(ImportMatchStatus.Linked, now);
        var row = Values;
        var filled = new List<string>();
        void Fill<T>(T? current, T value, Action<T> set, string name) where T : struct
        {
            if (current is not null) return;
            set(value);
            filled.Add(name);
        }

        Fill(trade.Direction, row.Direction, v => trade.Direction = v, nameof(Trade.Direction));
        Fill(trade.EntryTime, row.EntryTime, v => trade.EntryTime = v, nameof(Trade.EntryTime));
        Fill(trade.EntryPrice, row.EntryPrice, v => trade.EntryPrice = v, nameof(Trade.EntryPrice));
        Fill(trade.ExitPrice, row.ExitPrice, v => trade.ExitPrice = v, nameof(Trade.ExitPrice));
        Fill(trade.RiskAmount, row.RiskAmount, v => trade.RiskAmount = v, nameof(Trade.RiskAmount));
        Fill(trade.PnL, row.PnL, v => trade.PnL = v, nameof(Trade.PnL));
        Fill(trade.RR, row.RR, v => trade.RR = v, nameof(Trade.RR));
        if (row.TradeDuration > 0) Fill(trade.TradeDuration, row.TradeDuration, v => trade.TradeDuration = v, nameof(Trade.TradeDuration));
        if (string.IsNullOrWhiteSpace(trade.Session) && !string.IsNullOrWhiteSpace(row.Session))
        {
            trade.Session = row.Session.Trim();
            filled.Add(nameof(Trade.Session));
        }
        if (trade.StrategyId is null && strategyId is not null)
        {
            trade.StrategyId = strategyId;
            filled.Add("Strategy");
        }
        trade.ApplyDerivedValues();
        trade.UpdatedAt = now;
        return filled;
    }

    // Marked before the new trade is saved, so the decision and the trade are written together
    public void ImportSeparately(DateTime now) => Resolve(ImportMatchStatus.ImportedSeparately, now);

    public void RecordCreatedTrade(int tradeId) => CreatedTradeId = tradeId;

    public void Dismiss(DateTime now) => Resolve(ImportMatchStatus.Dismissed, now);

    private void Resolve(ImportMatchStatus status, DateTime now)
    {
        if (Status != ImportMatchStatus.Pending)
            throw new DomainException($"Row {RowNumber} was already handled ({Status}).");
        Status = status;
        ResolvedAt = now;
    }
}
