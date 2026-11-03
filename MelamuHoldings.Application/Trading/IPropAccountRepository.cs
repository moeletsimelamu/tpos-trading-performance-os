using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

// Prop firm accounts are owned per trader; every lookup is scoped to the owner and loads the history and rules
public interface IPropAccountRepository
{
    Task<PropFirmAccount?> GetForUserAsync(int id, int userId);
    Task<List<PropFirmAccount>> GetAllForUserAsync(int userId);
    void Add(PropFirmAccount account);
    Task SaveChangesAsync();
}
