using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public class TradeService
{
    private readonly ITradeRepository _tradeRepository;
    private readonly IStrategyRepository _strategyRepository;

    public TradeService(ITradeRepository tradeRepository, IStrategyRepository strategyRepository)
    {
        _tradeRepository = tradeRepository;
        _strategyRepository = strategyRepository;
    }

    public async Task<IEnumerable<TradeResponseDto>> GetTradesByUserAsync(int userId)
    {
        var trades = await _tradeRepository.GetByUserIdAsync(userId);
        return trades.Select(MapToResponseDto);
    }

    public async Task<TradeResponseDto?> GetTradeByIdAsync(int tradeId, int userId)
    {
        var trade = await _tradeRepository.GetForUserAsync(tradeId, userId);
        return trade is null ? null : MapToResponseDto(trade);
    }

    public async Task<TradeResponseDto> CreateTradeAsync(CreateTradeDto dto, int userId)
    {
        var strategy = await ResolveStrategyAsync(dto.StrategyName);

        var trade = new Trade
        {
            UserId = userId,
            StrategyId = strategy?.Id,
            Strategy = strategy,
            TradeDate = dto.TradeDate,
            EntryTime = dto.EntryTime,
            Instrument = dto.Instrument,
            Direction = Enum.Parse<TradeDirection>(dto.Direction, ignoreCase: true),
            EntryPrice = dto.EntryPrice,
            ExitPrice = dto.ExitPrice,
            RiskAmount = dto.RiskAmount,
            PnL = dto.PnL,
            RR = dto.RR,
            Session = dto.Session,
            DayOfWeek = dto.DayOfWeek,
            TradeDuration = dto.TradeDuration,
            IsManualOverride = dto.IsManualOverride,
            DeviationNotes = dto.DeviationNotes,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow
        };

        await _tradeRepository.AddAsync(trade);
        await _tradeRepository.SaveChangesAsync();
        return MapToResponseDto(trade);
    }

    public async Task<bool> UpdateTradeAsync(int tradeId, int userId, UpdateTradeDto dto)
    {
        var exists = await _tradeRepository.ExistsAsync(tradeId, userId);
        if (!exists) return false;

        var trade = await _tradeRepository.GetByIdAsync(tradeId);
        if (trade is null) return false;

        var strategy = await ResolveStrategyAsync(dto.StrategyName);

        trade.StrategyId = strategy?.Id;
        trade.TradeDate = dto.TradeDate;
        trade.EntryTime = dto.EntryTime;
        trade.Instrument = dto.Instrument;
        trade.Direction = Enum.Parse<TradeDirection>(dto.Direction, ignoreCase: true);
        trade.EntryPrice = dto.EntryPrice;
        trade.ExitPrice = dto.ExitPrice;
        trade.RiskAmount = dto.RiskAmount;
        trade.PnL = dto.PnL;
        trade.RR = dto.RR;
        trade.Session = dto.Session;
        trade.DayOfWeek = dto.DayOfWeek;
        trade.TradeDuration = dto.TradeDuration;
        trade.IsManualOverride = dto.IsManualOverride;
        trade.DeviationNotes = dto.DeviationNotes;
        trade.Notes = dto.Notes;

        await _tradeRepository.UpdateAsync(trade);
        await _tradeRepository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteTradeAsync(int tradeId, int userId)
    {
        var exists = await _tradeRepository.ExistsAsync(tradeId, userId);
        if (!exists) return false;

        var trade = await _tradeRepository.GetByIdAsync(tradeId);
        if (trade is null) return false;

        await _tradeRepository.DeleteAsync(trade);
        await _tradeRepository.SaveChangesAsync();
        return true;
    }

    // --- Private helpers ---

    private async Task<Strategy?> ResolveStrategyAsync(string? strategyName)
    {
        if (string.IsNullOrWhiteSpace(strategyName)) return null;

        var strategy = await _strategyRepository.GetByNameAsync(strategyName);

        if (strategy is null)
        {
            strategy = new Strategy
            {
                Name = strategyName,
                CreatedAt = DateTime.UtcNow
            };
            await _strategyRepository.AddAsync(strategy);
            await _strategyRepository.SaveChangesAsync();
        }

        return strategy;
    }

    private static TradeResponseDto MapToResponseDto(Trade trade) => new()
    {
        Id = trade.Id,
        TradeDate = trade.TradeDate,
        EntryTime = trade.EntryTime,
        Instrument = trade.Instrument,
        Direction = trade.Direction.ToString(),
        EntryPrice = trade.EntryPrice,
        ExitPrice = trade.ExitPrice,
        RiskAmount = trade.RiskAmount,
        PnL = trade.PnL,
        RR = trade.RR,
        StrategyName = trade.Strategy?.Name,
        Session = trade.Session,
        DayOfWeek = trade.DayOfWeek,
        TradeDuration = trade.TradeDuration,
        IsManualOverride = trade.IsManualOverride,
        DeviationNotes = trade.DeviationNotes,
        Notes = trade.Notes
    };
}
