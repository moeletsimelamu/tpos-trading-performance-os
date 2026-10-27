using MelamuHoldings.Domain.Entities;

namespace MelamuHoldings.Application.Interfaces;

public interface IUploadLogRepository : IRepository<UploadLog>
{
    Task<IEnumerable<UploadLog>> GetByUserIdAsync(int userId);
}