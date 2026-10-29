using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Trading;
using MelamuHoldings.Infrastructure.Persistence;

namespace MelamuHoldings.Infrastructure.Persistence.Repositories;

public partial class TradeRepository : BaseRepository<Trade>, ITradeRepository
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

    public async Task<Trade?> GetByIdempotencyKeyAsync(int userId, string idempotencyKey) =>
        await _context.Trades
            .Include(t => t.Strategy)
            .FirstOrDefaultAsync(t => t.UserId == userId && t.IdempotencyKey == idempotencyKey);

    // A duplicate idempotency key becomes UniqueConstraintViolationException, so the service can return the original
    public override async Task<int> SaveChangesAsync()
    {
        try
        {
            return await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            _context.ChangeTracker.Clear();
            var name = IndexNamePattern().Match(sql.Message) is { Success: true } m ? m.Groups[1].Value : "unknown";
            throw new UniqueConstraintViolationException(name, ex);
        }
    }

    [GeneratedRegex(@"(?:index|constraint) '([^']+)'")]
    private static partial Regex IndexNamePattern();
}
