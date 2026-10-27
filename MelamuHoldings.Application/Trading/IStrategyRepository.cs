using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public interface IStrategyRepository : IRepository<Strategy>
{
    Task<Strategy?> GetByNameAsync(string name);
}
