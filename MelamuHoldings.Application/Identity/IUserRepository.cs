using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Identity;

namespace MelamuHoldings.Application.Identity;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email);
    Task<bool> EmailExistsAsync(string email);
}
