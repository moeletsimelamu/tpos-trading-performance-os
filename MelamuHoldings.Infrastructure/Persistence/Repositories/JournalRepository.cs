using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Trading.Journal;

namespace MelamuHoldings.Infrastructure.Persistence.Repositories;

public partial class JournalRepository : IJournalRepository
{
    private readonly AppDbContext _context;

    public JournalRepository(AppDbContext context) => _context = context;

    public Task<List<JournalTemplate>> GetTemplatesAsync(int userId) =>
        _context.JournalTemplates.Include(t => t.Versions).Where(t => t.UserId == userId).ToListAsync();

    public Task<JournalTemplate?> GetTemplateAsync(int templateId, int userId) =>
        _context.JournalTemplates.Include(t => t.Versions).FirstOrDefaultAsync(t => t.Id == templateId && t.UserId == userId);

    public async Task<(JournalTemplateVersion Version, JournalTemplate Template)?> GetVersionAsync(int versionId, int userId)
    {
        var template = await _context.JournalTemplates.Include(t => t.Versions)
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Versions.Any(v => v.Id == versionId));
        return template is null ? null : (template.Versions.Single(v => v.Id == versionId), template);
    }

    public Task<TradeJournalEntry?> GetEntryAsync(int tradeId) =>
        _context.TradeJournalEntries.FirstOrDefaultAsync(e => e.TradeId == tradeId);

    public Task<List<TradeJournalEntry>> GetEntriesAsync(IReadOnlyCollection<int> tradeIds) =>
        _context.TradeJournalEntries.Where(e => tradeIds.Contains(e.TradeId)).ToListAsync();

    public Task<Dictionary<int, int>> CountEntriesByVersionAsync(int templateId) =>
        _context.TradeJournalEntries
            .Where(e => _context.JournalTemplateVersions.Any(v => v.Id == e.JournalTemplateVersionId && v.JournalTemplateId == templateId))
            .GroupBy(e => e.JournalTemplateVersionId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());

    public void Add(JournalTemplate template) => _context.JournalTemplates.Add(template);
    public void Add(TradeJournalEntry entry) => _context.TradeJournalEntries.Add(entry);

    public async Task SaveChangesAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            _context.ChangeTracker.Clear();
            var name = IndexNamePattern().Match(sql.Message) is { Success: true } m ? m.Groups[1].Value : "unknown";
            throw new UniqueConstraintViolationException(name, ex);
        }
    }

    [GeneratedRegex(@"(?:index|constraint) '([^']+)'")]
    private static partial Regex IndexNamePattern();
}
