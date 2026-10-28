using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

// Strategies are owned per trader; every lookup is scoped to the owner and loads the rules
public interface IStrategyRepository : IRepository<Strategy>
{
    Task<Strategy?> GetByNameAsync(int userId, string name);
    Task<Strategy?> GetForUserAsync(int strategyId, int userId);
    Task<List<Strategy>> GetAllForUserAsync(int userId);
    Task<Dictionary<int, int>> CountTradesAsync(int userId);

    // The trader's strategy with this name, created (with no rules) if it does not exist yet.
    // Saves immediately; a concurrent create of the same name returns the one that won.
    Task<Strategy> GetOrCreateAsync(int userId, string name, DateTime now);

    // Checklists with their items and every response
    Task<TradeChecklist?> GetChecklistAsync(int tradeId, int strategyId);
    Task<List<TradeChecklist>> GetChecklistsAsync(IReadOnlyCollection<int> tradeIds);
    void Add(TradeChecklist checklist);
}
