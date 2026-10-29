using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Application.Identity;
using MelamuHoldings.Domain.Identity;
using MelamuHoldings.Infrastructure.Persistence;

namespace MelamuHoldings.Infrastructure.Persistence.Repositories;

public class UserRepository : BaseRepository<User>, IUserRepository
{
    public UserRepository(AppDbContext context) : base(context) { }

    public async Task<User?> GetByEmailAsync(string email) =>
        await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

    public async Task<bool> EmailExistsAsync(string email) =>
        await _context.Users
            .AnyAsync(u => u.Email.ToLower() == email.ToLower());
}
