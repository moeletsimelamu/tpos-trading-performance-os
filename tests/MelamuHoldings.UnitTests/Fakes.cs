using MelamuHoldings.Application.Identity;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Identity;
using MelamuHoldings.Domain.Trading;
using MelamuHoldings.Domain.Trading.Journal;

namespace MelamuHoldings.UnitTests;

internal class InMemoryTradeRepository : ITradeRepository
{
    private readonly List<Trade> _trades;

    public InMemoryTradeRepository(params Trade[] trades) => _trades = trades.ToList();

    // Most analytics tests need only trades: they get a scope with no checklists and no journal answers
    public static implicit operator AnalyticsScope(InMemoryTradeRepository trades) => new(trades, new InMemoryChecklists(), new InMemoryJournal());

    public Task<IEnumerable<Trade>> GetByUserIdAsync(int userId) =>
        Task.FromResult<IEnumerable<Trade>>(_trades.Where(t => t.UserId == userId).ToList());

    public Task<IEnumerable<Trade>> GetByUserIdAndDateRangeAsync(int userId, DateOnly from, DateOnly to) =>
        Task.FromResult<IEnumerable<Trade>>(_trades.Where(t => t.UserId == userId && t.TradeDate >= from && t.TradeDate <= to).ToList());

    public Task<IEnumerable<Trade>> GetByStrategyAsync(int userId, int strategyId) =>
        Task.FromResult<IEnumerable<Trade>>(_trades.Where(t => t.UserId == userId && t.StrategyId == strategyId).ToList());

    public Task<Trade?> GetForUserAsync(int tradeId, int userId) =>
        Task.FromResult(_trades.FirstOrDefault(t => t.Id == tradeId && t.UserId == userId));

    public Task<bool> ExistsAsync(int tradeId, int userId) =>
        Task.FromResult(_trades.Any(t => t.Id == tradeId && t.UserId == userId));

    public Task<Trade?> GetByIdempotencyKeyAsync(int userId, string idempotencyKey) =>
        Task.FromResult(_trades.FirstOrDefault(t => t.UserId == userId && t.IdempotencyKey == idempotencyKey));

    public Task<Trade?> GetByIdAsync(int id) => Task.FromResult(_trades.FirstOrDefault(t => t.Id == id));
    public Task<IEnumerable<Trade>> GetAllAsync() => Task.FromResult<IEnumerable<Trade>>(_trades);
    public Task AddAsync(Trade entity) { _trades.Add(entity); return Task.CompletedTask; }
    public Task UpdateAsync(Trade entity) => Task.CompletedTask;
    public Task DeleteAsync(Trade entity) { _trades.Remove(entity); return Task.CompletedTask; }
    public Task<int> SaveChangesAsync() => Task.FromResult(0);
}

internal class InMemoryUserRepository : IUserRepository
{
    public List<User> Users { get; } = new();

    public Task<User?> GetByEmailAsync(string email) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> EmailExistsAsync(string email) =>
        Task.FromResult(Users.Any(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));

    public Task<User?> GetByIdAsync(int id) => Task.FromResult(Users.FirstOrDefault(u => u.Id == id));
    public Task<IEnumerable<User>> GetAllAsync() => Task.FromResult<IEnumerable<User>>(Users);
    public Task AddAsync(User entity) { entity.Id = Users.Count + 1; Users.Add(entity); return Task.CompletedTask; }
    public Task UpdateAsync(User entity) => Task.CompletedTask;
    public Task DeleteAsync(User entity) { Users.Remove(entity); return Task.CompletedTask; }
    public Task<int> SaveChangesAsync() => Task.FromResult(0);
}

internal static class TradeFactory
{
    private static int _nextId = 1;

    public static Trade Trade(
        string date, string time, decimal pnl, decimal rr = 0, string? strategy = null,
        bool manualOverride = false, string session = "New York", int userId = 1) => new()
    {
        Id = Interlocked.Increment(ref _nextId),
        UserId = userId,
        TradeDate = DateOnly.Parse(date),
        EntryTime = TimeOnly.Parse(time),
        Instrument = "NQ",
        Direction = TradeDirection.Long,
        EntryPrice = 100,
        ExitPrice = 101,
        RiskAmount = 100,
        PnL = pnl,
        RR = rr,
        Strategy = strategy is null ? null : new Strategy(userId, strategy, null, DateTime.UtcNow),
        Session = session,
        DayOfWeek = DateOnly.Parse(date).DayOfWeek.ToString(),
        IsManualOverride = manualOverride
    };
}

// Checklists for analytics filters and adherence; the strategy lookups are not needed there
internal sealed class InMemoryChecklists(params TradeChecklist[] checklists) : IStrategyRepository
{
    public Task<List<TradeChecklist>> GetChecklistsAsync(IReadOnlyCollection<int> tradeIds) =>
        Task.FromResult(checklists.Where(c => tradeIds.Contains(c.TradeId)).ToList());

    public Task<List<Strategy>> GetAllForUserAsync(int userId) => Task.FromResult(new List<Strategy>());
    public Task<Strategy?> GetByNameAsync(int userId, string name) => throw new NotSupportedException();
    public Task<Strategy?> GetForUserAsync(int strategyId, int userId) => throw new NotSupportedException();
    public Task<Dictionary<int, int>> CountTradesAsync(int userId) => throw new NotSupportedException();
    public Task<Strategy> GetOrCreateAsync(int userId, string name, DateTime now) => throw new NotSupportedException();
    public Task<TradeChecklist?> GetChecklistAsync(int tradeId, int strategyId) => throw new NotSupportedException();
    public void Add(TradeChecklist checklist) => throw new NotSupportedException();
    public Task<Strategy?> GetByIdAsync(int id) => throw new NotSupportedException();
    public Task<IEnumerable<Strategy>> GetAllAsync() => throw new NotSupportedException();
    public Task AddAsync(Strategy entity) => throw new NotSupportedException();
    public Task UpdateAsync(Strategy entity) => throw new NotSupportedException();
    public Task DeleteAsync(Strategy entity) => throw new NotSupportedException();
    public Task<int> SaveChangesAsync() => throw new NotSupportedException();
}

// Journal answers and templates for analytics filters
internal sealed class InMemoryJournal(IEnumerable<TradeJournalEntry>? entries = null, IEnumerable<JournalTemplate>? templates = null) : IJournalRepository
{
    private readonly List<TradeJournalEntry> _entries = entries?.ToList() ?? [];
    private readonly List<JournalTemplate> _templates = templates?.ToList() ?? [];

    public Task<List<TradeJournalEntry>> GetEntriesAsync(IReadOnlyCollection<int> tradeIds) =>
        Task.FromResult(_entries.Where(e => tradeIds.Contains(e.TradeId)).ToList());
    public Task<List<JournalTemplate>> GetTemplatesAsync(int userId) => Task.FromResult(_templates.Where(t => t.UserId == userId).ToList());

    public Task<JournalTemplate?> GetTemplateAsync(int templateId, int userId) => throw new NotSupportedException();
    public Task<(JournalTemplateVersion Version, JournalTemplate Template)?> GetVersionAsync(int versionId, int userId) => throw new NotSupportedException();
    public Task<TradeJournalEntry?> GetEntryAsync(int tradeId) => throw new NotSupportedException();
    public Task<Dictionary<int, int>> CountEntriesByVersionAsync(int templateId) => throw new NotSupportedException();
    public void Add(JournalTemplate template) => throw new NotSupportedException();
    public void Add(TradeJournalEntry entry) => throw new NotSupportedException();
    public Task SaveChangesAsync() => throw new NotSupportedException();
}
