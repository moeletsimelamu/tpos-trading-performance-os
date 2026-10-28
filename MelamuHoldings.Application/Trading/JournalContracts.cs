using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MelamuHoldings.Domain.Trading.Journal;

namespace MelamuHoldings.Application.Trading;

public interface IJournalRepository
{
    Task<List<JournalTemplate>> GetTemplatesAsync(int userId);
    Task<JournalTemplate?> GetTemplateAsync(int templateId, int userId);

    // The version with its template, so ownership can be checked
    Task<(JournalTemplateVersion Version, JournalTemplate Template)?> GetVersionAsync(int versionId, int userId);
    Task<TradeJournalEntry?> GetEntryAsync(int tradeId);
    Task<Dictionary<int, int>> CountEntriesByVersionAsync(int templateId);

    void Add(JournalTemplate template);
    void Add(TradeJournalEntry entry);

    // Throws UniqueConstraintViolationException when a unique index rejects the write
    Task SaveChangesAsync();
}

public record JournalVersionDto(int Id, int Version, DateTime CreatedAt, int EntryCount);

public record JournalTemplateDto(
    int Id, string Name, string? Description, bool IsDefault, JournalTemplateStatus Status,
    int CurrentVersionId, int CurrentVersion, IReadOnlyList<JournalSection> Sections,
    DateTime CreatedAt, DateTime? UpdatedAt, IReadOnlyList<JournalVersionDto> Versions);

// A trade's answers together with the questions they answer: always those of the version they were saved with
public record TradeJournalDto(
    int TemplateId, string TemplateName, int TemplateVersionId, int Version, int LatestVersion,
    IReadOnlyList<JournalSection> Sections, IReadOnlyDictionary<string, JsonElement> Answers);

public class SaveTemplateRequest
{
    [Required, MaxLength(JournalTemplate.MaxNameLength)] public string Name { get; set; } = string.Empty;
    [MaxLength(JournalTemplate.MaxDescriptionLength)] public string? Description { get; set; }
    [Required] public List<JournalSection> Sections { get; set; } = new();
}

public class DuplicateTemplateRequest
{
    [MaxLength(JournalTemplate.MaxNameLength)] public string? Name { get; set; }
}

public class JournalAnswersRequest
{
    // The template version the answers were written against
    [Required] public int? TemplateVersionId { get; set; }
    public Dictionary<string, JsonElement>? Answers { get; set; }
}
