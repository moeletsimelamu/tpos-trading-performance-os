using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Trading;
using MelamuHoldings.Infrastructure.Persistence;

namespace MelamuHoldings.Infrastructure.Persistence.Repositories;

public class StrategyRepository : BaseRepository<Strategy>, IStrategyRepository
{
    public StrategyRepository(AppDbContext context) : base(context) { }

    public async Task<Strategy?> GetByNameAsync(string name) =>
        await _context.Strategies
            .FirstOrDefaultAsync(s => s.Name.ToLower() == name.ToLower());
}
