using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public interface IUploadLogRepository : IRepository<UploadLog>
{
    Task<IEnumerable<UploadLog>> GetByUserIdAsync(int userId);
}
