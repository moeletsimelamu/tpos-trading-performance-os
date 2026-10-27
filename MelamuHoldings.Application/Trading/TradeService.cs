using MelamuHoldings.Application.Common;
using MelamuHoldings.Application.Finance;
using MelamuHoldings.Domain.Finance;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

// Trade journal entries: create (as draft, open or closed), edit, review, delete. A status is only saved
// once the fields it needs are there (Trade.MissingFor); nothing is filled in with placeholder values.
public class TradeService
{
    private const string IdempotencyIndex = "IX_Trades_UserId_IdempotencyKey";

    private readonly ITradeRepository _tradeRepository;
    private readonly IStrategyRepository _strategyRepository;
    private readonly TimeProvider _clock;

    public TradeService(ITradeRepository tradeRepository, IStrategyRepository strategyRepository, TimeProvider clock)
    {
        _tradeRepository = tradeRepository;
        _strategyRepository = strategyRepository;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<IEnumerable<TradeResponseDto>> GetTradesByUserAsync(int userId, TradeStatus? status = null)
    {
        var trades = await _tradeRepository.GetByUserIdAsync(userId);
        return trades.Where(t => status is null || t.Status == status).Select(MapToResponseDto);
    }

    public async Task<TradeResponseDto?> GetTradeByIdAsync(int tradeId, int userId)
    {
        var trade = await _tradeRepository.GetForUserAsync(tradeId, userId);
        return trade is null ? null : MapToResponseDto(trade);
    }

    // A retry with the same Idempotency-Key returns the trade it already created instead of saving a copy
    public async Task<TradeResult> CreateTradeAsync(CreateTradeDto dto, int userId, string? idempotencyKey = null)
    {
        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (key is not null && await _tradeRepository.GetByIdempotencyKeyAsync(userId, key) is { } previous)
            return new TradeResult(MapToResponseDto(previous), Replayed: true);

        var trade = new Trade { UserId = userId, CreatedAt = Now, IdempotencyKey = key };
        await ApplyAsync(trade, dto);
        trade.SetStatus(ParseStatus(dto.Status) ?? TradeStatus.Closed);

        try
        {
            await _tradeRepository.AddAsync(trade);
            await _tradeRepository.SaveChangesAsync();
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == IdempotencyIndex && key is not null)
        {
            var winner = await _tradeRepository.GetByIdempotencyKeyAsync(userId, key)
                ?? throw new ConflictException("The trade could not be saved. Please retry.");
            return new TradeResult(MapToResponseDto(winner), Replayed: true);
        }
        return new TradeResult(MapToResponseDto(trade), Replayed: false);
    }

    public async Task<TradeResponseDto?> UpdateTradeAsync(int tradeId, int userId, UpdateTradeDto dto)
    {
        var trade = await _tradeRepository.GetForUserAsync(tradeId, userId);
        if (trade is null) return null;

        await ApplyAsync(trade, dto);
        trade.SetStatus(ParseStatus(dto.Status) ?? trade.Status);
        trade.UpdatedAt = Now;

        await _tradeRepository.UpdateAsync(trade);
        await _tradeRepository.SaveChangesAsync();
        return MapToResponseDto(trade);
    }

    public async Task<TradeResponseDto?> ReviewTradeAsync(int tradeId, int userId, ReviewTradeDto dto)
    {
        var trade = await _tradeRepository.GetForUserAsync(tradeId, userId);
        if (trade is null) return null;

        trade.MarkReviewed(dto.ReviewNotes, Now);
        await _tradeRepository.UpdateAsync(trade);
        await _tradeRepository.SaveChangesAsync();
        return MapToResponseDto(trade);
    }

    public async Task<bool> DeleteTradeAsync(int tradeId, int userId)
    {
        var trade = await _tradeRepository.GetForUserAsync(tradeId, userId);
        if (trade is null) return false;

        await _tradeRepository.DeleteAsync(trade);
        await _tradeRepository.SaveChangesAsync();
        return true;
    }

    // --- Private helpers ---

    // Copies what was entered. Calculated values (R, duration) are cleared first so they are recalculated
    // from the new figures rather than kept stale.
    private async Task ApplyAsync(Trade trade, CreateTradeDto dto)
    {
        var strategy = await ResolveStrategyAsync(dto.StrategyName);
        trade.StrategyId = strategy?.Id;
        trade.Strategy = strategy;
        trade.TradeDate = dto.TradeDate;
        trade.Instrument = dto.Instrument.Trim();
        trade.TradingAccount = Blank(dto.TradingAccount);
        trade.Direction = dto.Direction is null ? null : Enum.Parse<TradeDirection>(dto.Direction, ignoreCase: true);
        trade.EntryTime = dto.EntryTime;
        trade.EntryPrice = dto.EntryPrice;
        trade.StopLoss = dto.StopLoss;
        trade.TakeProfit = dto.TakeProfit;
        trade.PositionSize = dto.PositionSize;
        trade.RiskAmount = dto.RiskAmount;
        trade.ExitDate = dto.ExitDate;
        trade.ExitTime = dto.ExitTime;
        trade.ExitPrice = dto.ExitPrice;
        trade.PnL = dto.PnL;
        trade.Fees = dto.Fees;
        trade.RR = dto.RR;
        trade.Currency = dto.Currency is null ? null : Currency.FromCode(dto.Currency).Code;
        trade.Session = dto.Session.Trim();
        trade.DayOfWeek = dto.DayOfWeek;
        trade.TradeDuration = dto.TradeDuration;
        trade.IsManualOverride = dto.IsManualOverride;
        trade.DeviationNotes = Blank(dto.DeviationNotes);
        trade.Notes = Blank(dto.Notes);
    }

    private static TradeStatus? ParseStatus(string? status) =>
        status is null ? null : Enum.Parse<TradeStatus>(status, ignoreCase: true);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<Strategy?> ResolveStrategyAsync(string? strategyName)
    {
        if (string.IsNullOrWhiteSpace(strategyName)) return null;

        var strategy = await _strategyRepository.GetByNameAsync(strategyName.Trim());

        if (strategy is null)
        {
            strategy = new Strategy
            {
                Name = strategyName.Trim(),
                CreatedAt = Now
            };
            await _strategyRepository.AddAsync(strategy);
            await _strategyRepository.SaveChangesAsync();
        }

        return strategy;
    }

    private static TradeResponseDto MapToResponseDto(Trade trade) => new()
    {
        Id = trade.Id,
        Status = trade.Status.ToString(),
        IsImported = trade.IsImported,
        TradeDate = trade.TradeDate,
        Instrument = trade.Instrument,
        TradingAccount = trade.TradingAccount,
        Direction = trade.Direction?.ToString(),
        EntryTime = trade.EntryTime,
        EntryPrice = trade.EntryPrice,
        StopLoss = trade.StopLoss,
        TakeProfit = trade.TakeProfit,
        PositionSize = trade.PositionSize,
        RiskAmount = trade.RiskAmount,
        ExitDate = trade.ExitDate,
        ExitTime = trade.ExitTime,
        ExitPrice = trade.ExitPrice,
        PnL = trade.PnL,
        Fees = trade.Fees,
        RR = trade.RR,
        Currency = trade.Currency,
        StrategyName = trade.Strategy?.Name,
        Session = trade.Session,
        DayOfWeek = trade.DayOfWeek,
        TradeDuration = trade.TradeDuration,
        IsManualOverride = trade.IsManualOverride,
        DeviationNotes = trade.DeviationNotes,
        Notes = trade.Notes,
        ReviewNotes = trade.ReviewNotes,
        ReviewedAt = trade.ReviewedAt,
        CreatedAt = trade.CreatedAt,
        UpdatedAt = trade.UpdatedAt
    };
}
