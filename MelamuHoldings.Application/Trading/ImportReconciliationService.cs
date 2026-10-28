using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public interface IImportMatchRepository
{
    Task<List<ImportMatch>> GetForUserAsync(int userId);
    Task<ImportMatch?> GetAsync(int id, int userId);
    void Add(ImportMatch match);

    // A concurrent change to the same match, or a second link to one trade, becomes ConflictException
    Task SaveChangesAsync();
}

public record MatchedTradeDto(
    int Id, TradeStatus Status, DateOnly TradeDate, string Instrument, string? Direction, TimeOnly? EntryTime,
    decimal? EntryPrice, decimal? ExitPrice, decimal? PnL, string? StrategyName);

// A CSV row and the journaled trade it matched; Trade is null if that trade has since been deleted
public record ImportMatchDto(
    int Id, int UploadLogId, string FileName, int RowNumber, ImportMatchStatus Status, ImportedRow Row,
    MatchedTradeDto? Trade, DateTime CreatedAt, DateTime? ResolvedAt, int? CreatedTradeId);

public record LinkResultDto(ImportMatchDto Match, IReadOnlyList<string> FilledFields);

// The trader's decision on each CSV row that matched a trade they journaled by hand
public class ImportReconciliationService
{
    private readonly IImportMatchRepository _matches;
    private readonly ITradeRepository _trades;
    private readonly IStrategyRepository _strategies;
    private readonly IUploadLogRepository _logs;
    private readonly TimeProvider _clock;

    public ImportReconciliationService(IImportMatchRepository matches, ITradeRepository trades, IStrategyRepository strategies,
        IUploadLogRepository logs, TimeProvider clock)
    {
        _matches = matches;
        _trades = trades;
        _strategies = strategies;
        _logs = logs;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<ImportMatchDto>> ListAsync(int userId, ImportMatchStatus? status)
    {
        var matches = (await _matches.GetForUserAsync(userId)).Where(m => status is null || m.Status == status).ToList();
        var trades = (await _trades.GetByUserIdAsync(userId)).ToDictionary(t => t.Id);
        var logs = (await _logs.GetByUserIdAsync(userId)).ToDictionary(l => l.Id);
        return matches.OrderByDescending(m => m.CreatedAt).ThenBy(m => m.RowNumber)
            .Select(m => ToDto(m, m.TradeId is { } id ? trades.GetValueOrDefault(id) : null, logs.GetValueOrDefault(m.UploadLogId)?.FileName))
            .ToList();
    }

    // Fills the journaled trade's empty fields from the row; nothing the trader recorded is changed
    public async Task<LinkResultDto> LinkAsync(int id, int userId)
    {
        var match = await MatchAsync(id, userId);
        var trade = match.TradeId is { } tradeId ? await _trades.GetForUserAsync(tradeId, userId) : null;
        if (trade is null) throw new Domain.Common.DomainException("The matched trade was deleted. Import the row as its own trade, or leave it out.");

        var row = match.Values;
        var strategyId = trade.StrategyId is null && !string.IsNullOrWhiteSpace(row.Strategy)
            ? (await _strategies.GetOrCreateAsync(userId, row.Strategy, Now)).Id
            : (int?)null;
        var filled = match.Link(trade, strategyId, Now);
        await _matches.SaveChangesAsync();
        return new LinkResultDto(ToDto(match, trade, await FileNameAsync(match, userId)), filled);
    }

    public async Task<ImportMatchDto> ImportSeparatelyAsync(int id, int userId)
    {
        var match = await MatchAsync(id, userId);
        var row = match.Values;
        var strategy = string.IsNullOrWhiteSpace(row.Strategy) ? null : await _strategies.GetOrCreateAsync(userId, row.Strategy, Now);
        var trade = ToTrade(row, userId, match.UploadLogId, strategy?.Id, Now);
        match.ImportSeparately(Now);
        await _trades.AddAsync(trade);
        if (await _logs.GetByIdAsync(match.UploadLogId) is { } log) log.ParsedRows++;

        // One save for the decision and the trade: a concurrent decision on the same row fails both
        await _matches.SaveChangesAsync();
        match.RecordCreatedTrade(trade.Id);
        await _matches.SaveChangesAsync();
        return await DtoAsync(match, userId);
    }

    public async Task<ImportMatchDto> DismissAsync(int id, int userId)
    {
        var match = await MatchAsync(id, userId);
        match.Dismiss(Now);
        await _matches.SaveChangesAsync();
        return await DtoAsync(match, userId);
    }

    // --- Shared with the CSV import ---

    public static ImportedRow ToRow(TradeCsvRecord r) => new(
        r.RowNumber, DateOnly.Parse(r.TradeDate), TimeOnly.TryParse(r.EntryTime, out var t) ? t : TimeOnly.MinValue,
        r.Instrument, Enum.Parse<TradeDirection>(r.Direction, ignoreCase: true), r.EntryPrice, r.ExitPrice, r.RiskAmount,
        r.PnL, r.RR, r.Strategy, r.Session, r.DayOfWeek, r.TradeDuration, r.IsManualOverride, r.DeviationNotes, r.Notes);

    public static Trade ToTrade(ImportedRow row, int userId, int uploadLogId, int? strategyId, DateTime now) => new()
    {
        UserId = userId,
        UploadLogId = uploadLogId,
        StrategyId = strategyId,
        TradeDate = row.TradeDate,
        EntryTime = row.EntryTime,
        Instrument = row.Instrument,
        Direction = row.Direction,
        EntryPrice = row.EntryPrice,
        ExitPrice = row.ExitPrice,
        RiskAmount = row.RiskAmount,
        PnL = row.PnL,
        RR = row.RR,
        Session = row.Session,
        DayOfWeek = row.DayOfWeek,
        TradeDuration = row.TradeDuration,
        IsManualOverride = row.IsManualOverride,
        DeviationNotes = row.DeviationNotes,
        Notes = row.Notes,
        CreatedAt = now
    };

    private async Task<ImportMatch> MatchAsync(int id, int userId) =>
        await _matches.GetAsync(id, userId) ?? throw new NotFoundException($"Import match {id} does not exist.");

    private async Task<ImportMatchDto> DtoAsync(ImportMatch match, int userId) =>
        ToDto(match, match.TradeId is { } tradeId ? await _trades.GetForUserAsync(tradeId, userId) : null, await FileNameAsync(match, userId));

    private async Task<string?> FileNameAsync(ImportMatch match, int userId) =>
        (await _logs.GetByUserIdAsync(userId)).FirstOrDefault(l => l.Id == match.UploadLogId)?.FileName;

    internal static ImportMatchDto ToDto(ImportMatch m, Trade? t, string? fileName) => new(
        m.Id, m.UploadLogId, fileName ?? string.Empty, m.RowNumber, m.Status, m.Values,
        t is null ? null : new MatchedTradeDto(t.Id, t.Status, t.TradeDate, t.Instrument, t.Direction?.ToString(), t.EntryTime,
            t.EntryPrice, t.ExitPrice, t.PnL, t.Strategy?.Name),
        m.CreatedAt, m.ResolvedAt, m.CreatedTradeId);
}
