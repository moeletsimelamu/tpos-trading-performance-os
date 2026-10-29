using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Infrastructure.Persistence.Repositories;

// Revisions are only added and read; the service checks the trade's owner first
public class TradeHistoryRepository : ITradeHistoryRepository
{
    private readonly AppDbContext _context;

    public TradeHistoryRepository(AppDbContext context) => _context = context;

    public void Add(TradeRevision revision) => _context.TradeRevisions.Add(revision);

    public Task<List<TradeRevision>> ListAsync(int tradeId) =>
        _context.TradeRevisions.AsNoTracking().Where(r => r.TradeId == tradeId).ToListAsync();
}
