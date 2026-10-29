using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Infrastructure.Persistence.Repositories;

public class ImportMatchRepository : IImportMatchRepository
{
    private readonly AppDbContext _context;

    public ImportMatchRepository(AppDbContext context) => _context = context;

    public Task<List<ImportMatch>> GetForUserAsync(int userId) =>
        _context.ImportMatches.Where(m => m.UserId == userId).ToListAsync();

    public Task<ImportMatch?> GetAsync(int id, int userId) =>
        _context.ImportMatches.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);

    public void Add(ImportMatch match) => _context.ImportMatches.Add(match);

    public async Task SaveChangesAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This row was decided in another request at the same moment. Reload the matches.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException("That trade is already linked to an imported row.");
        }
    }
}
