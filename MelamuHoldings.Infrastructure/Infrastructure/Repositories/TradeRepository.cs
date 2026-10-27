using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Application.Interfaces;
using MelamuHoldings.Domain.Entities;
using MelamuHoldings.Infrastructure.Data;

namespace MelamuHoldings.Infrastructure.Repositories;

public class TradeRepository : BaseRepository<Trade>, ITradeRepository
{
    public TradeRepository(AppDbContext context) : base(context) { }

    public async Task<IEnumerable<Trade>> GetByUserIdAsync(int userId) =>
        await _context.Trades
            .Include(t => t.Strategy)
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.TradeDate)
            .ThenByDescending(t => t.EntryTime)
            .ToListAsync();

    public async Task<IEnumerable<Trade>> GetByUserIdAndDateRangeAsync(
        int userId, DateOnly from, DateOnly to) =>
        await _context.Trades
            .Include(t => t.Strategy)
            .Where(t => t.UserId == userId
                     && t.TradeDate >= from
                     && t.TradeDate <= to)
            .OrderBy(t => t.TradeDate)
            .ThenBy(t => t.EntryTime)
            .ToListAsync();

    public async Task<IEnumerable<Trade>> GetByStrategyAsync(int userId, int strategyId) =>
        await _context.Trades
            .Where(t => t.UserId == userId && t.StrategyId == strategyId)
            .ToListAsync();

    public async Task<Trade?> GetForUserAsync(int tradeId, int userId) =>
        await _context.Trades
            .Include(t => t.Strategy)
            .FirstOrDefaultAsync(t => t.Id == tradeId && t.UserId == userId);

    public async Task<bool> ExistsAsync(int tradeId, int userId) =>
        await _context.Trades
            .AnyAsync(t => t.Id == tradeId && t.UserId == userId);
}