using MelamuHoldings.Domain.Entities;

namespace MelamuHoldings.Application.Interfaces;

public interface IStrategyRepository : IRepository<Strategy>
{
    Task<Strategy?> GetByNameAsync(string name);
}