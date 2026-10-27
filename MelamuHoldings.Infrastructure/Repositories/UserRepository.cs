using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Application.Interfaces;
using MelamuHoldings.Domain.Entities;
using MelamuHoldings.Infrastructure.Data;

namespace MelamuHoldings.Infrastructure.Repositories;

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