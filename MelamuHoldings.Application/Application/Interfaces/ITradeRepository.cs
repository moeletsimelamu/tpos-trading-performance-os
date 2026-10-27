using MelamuHoldings.Domain.Entities;

namespace MelamuHoldings.Application.Interfaces;

public interface ITradeRepository : IRepository<Trade>
{
    Task<IEnumerable<Trade>> GetByUserIdAsync(int userId);
    Task<IEnumerable<Trade>> GetByUserIdAndDateRangeAsync(int userId, DateOnly from, DateOnly to);
    Task<IEnumerable<Trade>> GetByStrategyAsync(int userId, int strategyId);
    Task<bool> ExistsAsync(int tradeId, int userId);
}