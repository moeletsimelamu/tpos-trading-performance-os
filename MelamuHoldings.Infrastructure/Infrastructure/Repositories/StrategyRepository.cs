using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Application.Interfaces;
using MelamuHoldings.Domain.Entities;
using MelamuHoldings.Infrastructure.Data;

namespace MelamuHoldings.Infrastructure.Repositories;

public class StrategyRepository : BaseRepository<Strategy>, IStrategyRepository
{
    public StrategyRepository(AppDbContext context) : base(context) { }

    public async Task<Strategy?> GetByNameAsync(string name) =>
        await _context.Strategies
            .FirstOrDefaultAsync(s => s.Name.ToLower() == name.ToLower());
}