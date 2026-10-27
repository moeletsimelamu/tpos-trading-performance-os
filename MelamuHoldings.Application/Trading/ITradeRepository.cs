using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public interface ITradeRepository : IRepository<Trade>
{
    Task<IEnumerable<Trade>> GetByUserIdAsync(int userId);
    Task<Trade?> GetForUserAsync(int tradeId, int userId);
    Task<IEnumerable<Trade>> GetByUserIdAndDateRangeAsync(int userId, DateOnly from, DateOnly to);
    Task<IEnumerable<Trade>> GetByStrategyAsync(int userId, int strategyId);
    Task<bool> ExistsAsync(int tradeId, int userId);
}
