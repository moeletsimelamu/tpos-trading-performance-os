using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Trading;
using MelamuHoldings.Infrastructure.Persistence;

namespace MelamuHoldings.Infrastructure.Persistence.Repositories;

public class UploadLogRepository : BaseRepository<UploadLog>, IUploadLogRepository
{
    public UploadLogRepository(AppDbContext context) : base(context) { }

    public async Task<IEnumerable<UploadLog>> GetByUserIdAsync(int userId) =>
        await _context.UploadLogs
            .Where(u => u.UserId == userId)
            .OrderByDescending(u => u.UploadedAt)
            .ToListAsync();
}
