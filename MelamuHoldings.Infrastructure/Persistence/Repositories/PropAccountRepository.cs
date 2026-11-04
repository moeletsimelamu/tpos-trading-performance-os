using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Infrastructure.Persistence.Repositories;

public partial class PropAccountRepository : IPropAccountRepository
{
    private readonly AppDbContext _context;

    public PropAccountRepository(AppDbContext context) => _context = context;

    private IQueryable<PropFirmAccount> Accounts =>
        _context.PropFirmAccounts.Include(a => a.Events).Include(a => a.RuleSets).Include(a => a.Balances).AsSplitQuery();

    public Task<PropFirmAccount?> GetForUserAsync(int id, int userId) =>
        Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);

    public Task<List<PropFirmAccount>> GetAllForUserAsync(int userId) =>
        Accounts.Where(a => a.UserId == userId).ToListAsync();

    public Task<List<LinkedTrade>> GetLinkedTradesAsync(int userId) =>
        _context.Trades.AsNoTracking()
            .Where(t => t.UserId == userId && t.PropFirmAccountId != null)
            .Select(t => new LinkedTrade(t.PropFirmAccountId!.Value, t.Id, t.Status, t.TradeDate, t.ExitDate, t.EntryTime, t.ExitTime, t.PnL))
            .ToListAsync();

    public void Add(PropFirmAccount account) => _context.PropFirmAccounts.Add(account);

    // A unique index rejecting the write becomes UniqueConstraintViolationException, named after the index;
    // a change made by another request since this one read the account is a conflict to reload and retry
    public async Task SaveChangesAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The account was changed by another request at the same time. Reload it and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            var name = IndexNamePattern().Match(sql.Message) is { Success: true } m ? m.Groups[1].Value : "unknown";
            throw new UniqueConstraintViolationException(name, ex);
        }
    }

    [GeneratedRegex(@"(?:index|constraint) '([^']+)'")]
    private static partial Regex IndexNamePattern();
}
