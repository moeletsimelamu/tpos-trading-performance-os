using System.Text.Json;
using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Trading.Journal;

public enum JournalTemplateStatus { Active, Archived }

// A reusable journal layout owned by one trader. Its questions live in numbered, immutable versions:
// changing the questions adds a version, and every entry keeps the version it was written with.
public class JournalTemplate
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 300;

    private readonly List<JournalTemplateVersion> _versions = new();

    public int Id { get; private set; }
    public int UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsDefault { get; private set; }
    public JournalTemplateStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyList<JournalTemplateVersion> Versions => _versions;
    public JournalTemplateVersion Current => _versions.MaxBy(v => v.Version) ?? throw new DomainException("The template has no version.");

    private JournalTemplate() { }

    public JournalTemplate(int userId, string name, string? description, IReadOnlyList<JournalSection> sections, DateTime now)
    {
        UserId = userId;
        SetDetails(name, description);
        Status = JournalTemplateStatus.Active;
        CreatedAt = now;
        _versions.Add(new JournalTemplateVersion(1, JournalSchema.Validate(sections), now));
    }

    // Renames in place; adds a version only when the questions actually change
    public bool Update(string name, string? description, IReadOnlyList<JournalSection> sections, DateTime now)
    {
        SetDetails(name, description);
        UpdatedAt = now;
        var validated = JournalSchema.Validate(sections);
        if (JournalSchema.Serialize(validated) == Current.Schema) return false;
        _versions.Add(new JournalTemplateVersion(Current.Version + 1, validated, now));
        return true;
    }

    public JournalTemplate Duplicate(string name, DateTime now) => new(UserId, name, Description, Current.Sections, now);

    public void Archive(DateTime now)
    {
        if (IsDefault) throw new DomainException("The default template cannot be archived. Make another template the default first.");
        Status = JournalTemplateStatus.Archived;
        UpdatedAt = now;
    }

    public void Restore(DateTime now)
    {
        Status = JournalTemplateStatus.Active;
        UpdatedAt = now;
    }

    public void MakeDefault(DateTime now)
    {
        if (Status == JournalTemplateStatus.Archived) throw new DomainException("Restore the template before making it the default.");
        IsDefault = true;
        UpdatedAt = now;
    }

    public void ClearDefault() => IsDefault = false;

    private void SetDetails(string name, string? description)
    {
        Name = Text.Required(name, MaxNameLength, "Template name");
        Description = Text.Optional(description, MaxDescriptionLength, "Template description");
    }
}

// One immutable set of questions. Schema is the validated sections as JSON.
public class JournalTemplateVersion
{
    public int Id { get; private set; }
    public int JournalTemplateId { get; private set; }
    public int Version { get; private set; }
    public string Schema { get; private set; } = "[]";
    public DateTime CreatedAt { get; private set; }

    public IReadOnlyList<JournalSection> Sections => JournalSchema.Parse(Schema);

    private JournalTemplateVersion() { }

    internal JournalTemplateVersion(int version, IReadOnlyList<JournalSection> sections, DateTime now)
    {
        Version = version;
        Schema = JournalSchema.Serialize(sections);
        CreatedAt = now;
    }
}

// The template answers of one trade, tied to the exact template version they were written against
public class TradeJournalEntry
{
    public int Id { get; private set; }
    public int TradeId { get; private set; }
    public int JournalTemplateVersionId { get; private set; }
    public string Answers { get; private set; } = "{}";
    public DateTime UpdatedAt { get; private set; }

    private TradeJournalEntry() { }

    public TradeJournalEntry(int tradeId, JournalTemplateVersion version, IReadOnlyDictionary<string, JsonElement> answers, DateTime now)
    {
        TradeId = tradeId;
        Record(version, answers, now);
    }

    public IReadOnlyDictionary<string, JsonElement> AnswerValues =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Answers) ?? new();

    // Answers are validated against the version they are saved with; moving an entry to another version
    // is an explicit choice by the trader, made by saving against that version
    public void Record(JournalTemplateVersion version, IReadOnlyDictionary<string, JsonElement> answers, DateTime now)
    {
        var valid = JournalSchema.ValidateAnswers(version.Sections, answers);
        JournalTemplateVersionId = version.Id;
        Answers = JsonSerializer.Serialize(valid);
        UpdatedAt = now;
    }
}

// The template every trader starts with: the sections of the owner's brief that are not already trade fields.
// Trade identification and execution are always on the trade form; this adds context, checklist, psychology,
// review and learning. Every question can be edited.
public static class DefaultJournalTemplate
{
    public const string Name = "Standard trade journal";

    public static IReadOnlyList<JournalSection> Sections =>
    [
        new("context", "Market context and setup", "What the market was doing and why this was a setup.",
        [
            new("market-context", "Market context", JournalFieldType.LongText),
            new("setup", "Setup identified", JournalFieldType.ShortText),
            new("conditions", "Conditions required by the strategy that were present", JournalFieldType.LongText),
            new("chart-link", "Chart or screenshot link", JournalFieldType.Url, Help: "Uploading images arrives with the journal view (J5).")
        ]),
        new("pre-trade", "Pre-trade checklist", "Tick what was true before entry. Replace these with your own rules.",
        [
            new("checklist", "Before entry", JournalFieldType.Checklist, Options:
            [
                "Setup meets the defined criteria", "Required conditions are present", "Entry is justified by the strategy",
                "Stop loss is defined", "Risk is within the limit", "Trade complies with the account rules", "I am prepared to execute the plan"
            ])
        ]),
        new("psychology", "Psychology and behaviour", "Your own account of how you felt. Nothing here is inferred from the result.",
        [
            new("emotion-before", "Emotional state before entry", JournalFieldType.SingleSelect, Options: ["Calm", "Focused", "Anxious", "Frustrated", "Overconfident", "Tired"]),
            new("confidence", "Confidence before entry", JournalFieldType.Rating),
            new("emotion-during", "Emotional state during the trade", JournalFieldType.SingleSelect, Options: ["Calm", "Focused", "Anxious", "Frustrated", "Overconfident", "Tired"]),
            new("emotion-after", "Emotional state after exit", JournalFieldType.SingleSelect, Options: ["Calm", "Satisfied", "Relieved", "Frustrated", "Disappointed"]),
            new("influences", "What influenced the trade", JournalFieldType.MultiSelect, Options:
                ["Hesitation", "FOMO", "Frustration or revenge", "Pressure to recover losses", "None of these"]),
            new("followed-risk", "I followed the planned risk", JournalFieldType.Checkbox),
            new("reflections", "Reflections", JournalFieldType.LongText)
        ]),
        new("post-trade", "Post-trade review", "Separate the quality of the decision from the result.",
        [
            new("valid", "Was the trade valid according to the strategy?", JournalFieldType.SingleSelect, Required: true, Options: ["Yes", "No", "Partly"]),
            new("entry-correct", "Was the entry executed correctly?", JournalFieldType.SingleSelect, Options: ["Yes", "No", "Partly"]),
            new("risk-managed", "Was risk managed according to plan?", JournalFieldType.SingleSelect, Options: ["Yes", "No", "Partly"]),
            new("exit-plan", "Was the exit consistent with the plan?", JournalFieldType.SingleSelect, Options: ["Yes", "No", "Partly"]),
            new("went-well", "What went well?", JournalFieldType.LongText),
            new("went-wrong", "What went wrong?", JournalFieldType.LongText)
        ]),
        new("learning", "Review and learning", null,
        [
            new("quality", "Trade quality (not the result)", JournalFieldType.Rating),
            new("adherence", "Rule adherence", JournalFieldType.Rating),
            new("lesson", "Key lesson", JournalFieldType.ShortText, Required: true),
            new("follow-up", "Follow-up action", JournalFieldType.ShortText)
        ])
    ];
}
