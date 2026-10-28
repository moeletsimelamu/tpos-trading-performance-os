using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Trading.Journal;

namespace MelamuHoldings.Application.Trading;

// Journal templates: reusable, versioned sets of questions. A trader always has a default template; the
// standard one is created the first time they need one.
public class JournalTemplateService
{
    private const string OneDefaultIndex = "IX_JournalTemplates_OneDefault";

    private readonly IJournalRepository _repository;
    private readonly TimeProvider _clock;

    public JournalTemplateService(IJournalRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<JournalTemplateDto>> ListAsync(int userId, bool includeArchived)
    {
        await EnsureDefaultAsync(userId);
        var templates = await _repository.GetTemplatesAsync(userId);
        var result = new List<JournalTemplateDto>();
        foreach (var t in templates.Where(t => includeArchived || t.Status == JournalTemplateStatus.Active)
                     .OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name))
            result.Add(await ToDtoAsync(t));
        return result;
    }

    public async Task<JournalTemplateDto> GetAsync(int id, int userId) => await ToDtoAsync(await TemplateAsync(id, userId));

    // The questions of one past version, for reading entries written against it
    public async Task<IReadOnlyList<JournalSection>> GetVersionSectionsAsync(int id, int version, int userId)
    {
        var template = await TemplateAsync(id, userId);
        return (template.Versions.SingleOrDefault(v => v.Version == version)
            ?? throw new NotFoundException($"Template {id} has no version {version}.")).Sections;
    }

    public async Task<JournalTemplateDto> CreateAsync(SaveTemplateRequest request, int userId)
    {
        var template = new JournalTemplate(userId, request.Name, request.Description, request.Sections, Now);
        _repository.Add(template);
        await _repository.SaveChangesAsync();
        return await ToDtoAsync(template);
    }

    // Questions that change become a new version; existing entries keep the version they were written with
    public async Task<JournalTemplateDto> UpdateAsync(int id, SaveTemplateRequest request, int userId)
    {
        var template = await TemplateAsync(id, userId);
        template.Update(request.Name, request.Description, request.Sections, Now);
        await _repository.SaveChangesAsync();
        return await ToDtoAsync(template);
    }

    public async Task<JournalTemplateDto> DuplicateAsync(int id, DuplicateTemplateRequest request, int userId)
    {
        var source = await TemplateAsync(id, userId);
        var name = string.IsNullOrWhiteSpace(request.Name) ? $"{source.Name} (copy)" : request.Name;
        if (name.Length > JournalTemplate.MaxNameLength) name = name[..JournalTemplate.MaxNameLength];
        var copy = source.Duplicate(name, Now);
        _repository.Add(copy);
        await _repository.SaveChangesAsync();
        return await ToDtoAsync(copy);
    }

    public async Task<JournalTemplateDto> ArchiveAsync(int id, int userId)
    {
        var template = await TemplateAsync(id, userId);
        template.Archive(Now);
        await _repository.SaveChangesAsync();
        return await ToDtoAsync(template);
    }

    public async Task<JournalTemplateDto> RestoreAsync(int id, int userId)
    {
        var template = await TemplateAsync(id, userId);
        template.Restore(Now);
        await _repository.SaveChangesAsync();
        return await ToDtoAsync(template);
    }

    public async Task<JournalTemplateDto> MakeDefaultAsync(int id, int userId)
    {
        var template = await TemplateAsync(id, userId);
        if (!template.IsDefault)
        {
            // One default per trader (unique index): clear the old one first
            foreach (var other in (await _repository.GetTemplatesAsync(userId)).Where(t => t.IsDefault && t.Id != id))
                other.ClearDefault();
            await _repository.SaveChangesAsync();
            template.MakeDefault(Now);
            await _repository.SaveChangesAsync();
        }
        return await ToDtoAsync(template);
    }

    // A trader always has a default: until they have one, the standard template is created as it.
    // (Checked by default, not by count: a trader whose first act is creating their own template still gets one.)
    public async Task EnsureDefaultAsync(int userId)
    {
        if ((await _repository.GetTemplatesAsync(userId)).Any(t => t.IsDefault)) return;
        var template = new JournalTemplate(userId, DefaultJournalTemplate.Name,
            "The standard journal: context, checklist, psychology, review and learning. Edit it to match your own process.",
            DefaultJournalTemplate.Sections, Now);
        template.MakeDefault(Now);
        _repository.Add(template);
        try
        {
            await _repository.SaveChangesAsync();
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == OneDefaultIndex)
        {
            // Another request created it at the same moment
        }
    }

    private async Task<JournalTemplate> TemplateAsync(int id, int userId) =>
        await _repository.GetTemplateAsync(id, userId) ?? throw new NotFoundException($"Journal template {id} does not exist.");

    private async Task<JournalTemplateDto> ToDtoAsync(JournalTemplate t)
    {
        var counts = await _repository.CountEntriesByVersionAsync(t.Id);
        var current = t.Current;
        return new JournalTemplateDto(
            t.Id, t.Name, t.Description, t.IsDefault, t.Status, current.Id, current.Version, current.Sections, t.CreatedAt, t.UpdatedAt,
            t.Versions.OrderByDescending(v => v.Version)
                .Select(v => new JournalVersionDto(v.Id, v.Version, v.CreatedAt, counts.TryGetValue(v.Id, out var n) ? n : 0)).ToList());
    }
}

// Reads and writes the template answers of one trade
public class TradeJournal
{
    private readonly IJournalRepository _repository;

    public TradeJournal(IJournalRepository repository) => _repository = repository;

    public async Task<TradeJournalDto?> GetAsync(int tradeId, int userId)
    {
        var entry = await _repository.GetEntryAsync(tradeId);
        if (entry is null) return null;
        var found = await _repository.GetVersionAsync(entry.JournalTemplateVersionId, userId)
            ?? throw new DomainException("The template version of this journal entry is missing.");
        return new TradeJournalDto(found.Template.Id, found.Template.Name, found.Version.Id, found.Version.Version,
            found.Template.Current.Version, found.Version.Sections, entry.AnswerValues);
    }

    // Checks the answers against the chosen version (which must be the trader's own) without saving anything,
    // so a new trade is never saved with answers that would then be refused
    public async Task ValidateAsync(int userId, JournalAnswersRequest request)
    {
        var found = await _repository.GetVersionAsync(request.TemplateVersionId!.Value, userId)
            ?? throw new DomainException($"Template version {request.TemplateVersionId} does not exist.");
        JournalSchema.ValidateAnswers(found.Version.Sections, request.Answers);
    }

    // Validates and stages the answers; the caller saves them with the trade
    public async Task RecordAsync(int tradeId, int userId, JournalAnswersRequest request, DateTime now)
    {
        var found = await _repository.GetVersionAsync(request.TemplateVersionId!.Value, userId)
            ?? throw new DomainException($"Template version {request.TemplateVersionId} does not exist.");
        var answers = request.Answers ?? new();
        var entry = await _repository.GetEntryAsync(tradeId);
        if (entry is null) _repository.Add(new TradeJournalEntry(tradeId, found.Version, answers, now));
        else entry.Record(found.Version, answers, now);
    }

    // Required questions the trader has not answered yet, checked before a trade is marked reviewed
    public async Task<IReadOnlyList<string>> MissingRequiredAsync(int tradeId, int userId)
    {
        var entry = await _repository.GetEntryAsync(tradeId);
        if (entry is null) return [];
        var found = await _repository.GetVersionAsync(entry.JournalTemplateVersionId, userId);
        return found is null ? [] : JournalSchema.MissingRequired(found.Value.Version.Sections, entry.AnswerValues);
    }
}
