using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

// A trade linked to a prop firm account, with only what its standing needs
public record LinkedTrade(int AccountId, int TradeId, TradeStatus Status, DateOnly TradeDate, DateOnly? ExitDate, TimeOnly? EntryTime, TimeOnly? ExitTime, decimal? PnL);

// Prop firm accounts are owned per trader; every lookup is scoped to the owner and loads the history, rules and balances
public interface IPropAccountRepository
{
    Task<PropFirmAccount?> GetForUserAsync(int id, int userId);
    Task<List<PropFirmAccount>> GetAllForUserAsync(int userId);
    Task<List<LinkedTrade>> GetLinkedTradesAsync(int userId);
    void Add(PropFirmAccount account);
    Task SaveChangesAsync();
}
