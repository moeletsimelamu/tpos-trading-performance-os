using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Trading;
using MelamuHoldings.Infrastructure.Persistence;

namespace MelamuHoldings.Infrastructure.Persistence.Repositories;

public partial class StrategyRepository : BaseRepository<Strategy>, IStrategyRepository
{
    public StrategyRepository(AppDbContext context) : base(context) { }

    private IQueryable<Strategy> WithRules => _context.Strategies.Include(s => s.Rules);

    // Names compare case-insensitively (the column's collation)
    public Task<Strategy?> GetByNameAsync(int userId, string name) =>
        WithRules.FirstOrDefaultAsync(s => s.UserId == userId && s.Name == name);

    public Task<Strategy?> GetForUserAsync(int strategyId, int userId) =>
        WithRules.FirstOrDefaultAsync(s => s.Id == strategyId && s.UserId == userId);

    public Task<List<Strategy>> GetAllForUserAsync(int userId) =>
        WithRules.Where(s => s.UserId == userId).ToListAsync();

    public Task<Dictionary<int, int>> CountTradesAsync(int userId) =>
        _context.Trades.Where(t => t.UserId == userId && t.StrategyId != null)
            .GroupBy(t => t.StrategyId!.Value)
            .ToDictionaryAsync(g => g.Key, g => g.Count());

    public async Task<Strategy> GetOrCreateAsync(int userId, string name, DateTime now)
    {
        name = name.Trim();
        if (await GetByNameAsync(userId, name) is { } existing) return existing;

        var strategy = new Strategy(userId, name, null, now);
        _context.Strategies.Add(strategy);
        try
        {
            await _context.SaveChangesAsync();
            return strategy;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Another request created it first: use that one
            _context.Entry(strategy).State = EntityState.Detached;
            return await GetByNameAsync(userId, name) ?? throw new ConflictException($"Strategy '{name}' could not be saved. Please retry.");
        }
    }

    private IQueryable<TradeChecklist> Checklists =>
        _context.TradeChecklists.Include(c => c.Items).ThenInclude(i => i.Responses);

    public Task<TradeChecklist?> GetChecklistAsync(int tradeId, int strategyId) =>
        Checklists.FirstOrDefaultAsync(c => c.TradeId == tradeId && c.StrategyId == strategyId);

    public Task<List<TradeChecklist>> GetChecklistsAsync(IReadOnlyCollection<int> tradeIds) =>
        Checklists.Where(c => tradeIds.Contains(c.TradeId)).AsSplitQuery().ToListAsync();

    public void Add(TradeChecklist checklist) => _context.TradeChecklists.Add(checklist);

    // A unique index rejecting the write becomes UniqueConstraintViolationException, named after the index
    public override async Task<int> SaveChangesAsync()
    {
        try
        {
            return await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            var name = IndexNamePattern().Match(sql.Message) is { Success: true } m ? m.Groups[1].Value : "unknown";
            throw new UniqueConstraintViolationException(name, ex);
        }
    }

    [GeneratedRegex(@"(?:index|constraint) '([^']+)'")]
    private static partial Regex IndexNamePattern();
}
