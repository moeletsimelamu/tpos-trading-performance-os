using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public interface IUploadLogRepository : IRepository<UploadLog>
{
    Task<IEnumerable<UploadLog>> GetByUserIdAsync(int userId);

    // Everything saved through any repository inside this either all stays or is all undone
    Task<T> InTransactionAsync<T>(Func<Task<T>> work);

    // Written straight to the database, so nothing a failed import left pending is saved with it
    Task MarkFailedAsync(int id);
}
