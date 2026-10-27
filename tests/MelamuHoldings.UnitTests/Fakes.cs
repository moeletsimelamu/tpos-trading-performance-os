using MelamuHoldings.Application.Interfaces;
using MelamuHoldings.Domain.Entities;
using MelamuHoldings.Domain.Enums;

namespace MelamuHoldings.UnitTests;

internal class InMemoryTradeRepository : ITradeRepository
{
    private readonly List<Trade> _trades;

    public InMemoryTradeRepository(params Trade[] trades) => _trades = trades.ToList();

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
        Strategy = strategy is null ? null : new Strategy { Name = strategy },
        Session = session,
        DayOfWeek = DateOnly.Parse(date).DayOfWeek.ToString(),
        IsManualOverride = manualOverride
    };
}
