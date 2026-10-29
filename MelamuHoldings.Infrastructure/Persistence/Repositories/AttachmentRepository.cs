using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Infrastructure.Persistence.Repositories;

// Attachments are looked up by trade; the service checks the trade's owner first
public class AttachmentRepository : IAttachmentRepository
{
    private readonly AppDbContext _context;

    public AttachmentRepository(AppDbContext context) => _context = context;

    public Task<List<TradeAttachment>> ListAsync(int tradeId) =>
        _context.TradeAttachments.Where(a => a.TradeId == tradeId).ToListAsync();

    public Task<TradeAttachment?> GetAsync(int tradeId, int attachmentId) =>
        _context.TradeAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.TradeId == tradeId);

    public void Add(TradeAttachment attachment) => _context.TradeAttachments.Add(attachment);
    public void Remove(TradeAttachment attachment) => _context.TradeAttachments.Remove(attachment);
    public Task SaveChangesAsync() => _context.SaveChangesAsync();
}
