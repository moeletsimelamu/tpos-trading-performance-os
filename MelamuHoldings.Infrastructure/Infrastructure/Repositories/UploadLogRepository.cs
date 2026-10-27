using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Application.Interfaces;
using MelamuHoldings.Domain.Entities;
using MelamuHoldings.Infrastructure.Data;

namespace MelamuHoldings.Infrastructure.Repositories;

public class UploadLogRepository : BaseRepository<UploadLog>, IUploadLogRepository
{
    public UploadLogRepository(AppDbContext context) : base(context) { }

    public async Task<IEnumerable<UploadLog>> GetByUserIdAsync(int userId) =>
        await _context.UploadLogs
            .Where(u => u.UserId == userId)
            .OrderByDescending(u => u.UploadedAt)
            .ToListAsync();
}