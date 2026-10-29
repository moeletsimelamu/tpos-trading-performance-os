using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Trading.Journal;

// The field types a journal template can use. Attachments arrive with J5; strategy rules with J4.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JournalFieldType
{
    ShortText,
    LongText,
    Number,
    DateTime,
    SingleSelect,
    MultiSelect,
    Checkbox,
    Checklist,
    Rating,
    Url
}

// One question in a template. The id is stable across versions, so an answer can be carried from one
// version to the next when the trader chooses to; options hold the choices or checklist items.
public sealed record JournalField(
    string Id, string Label, JournalFieldType Type, bool Required = false, string? Help = null, IReadOnlyList<string>? Options = null);

public sealed record JournalSection(string Id, string Title, string? Description, IReadOnlyList<JournalField> Fields);

// Validates template definitions and the answers recorded against them. Answers are always checked against
// the exact version an entry was written with, so a later template change can never reinterpret them.
public static partial class JournalSchema
{
    public const int MaxSections = 12;
    public const int MaxFields = 80;
    public const int MaxOptions = 30;
    public const int MaxShortText = 200;
    public const int MaxLongText = 4000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyList<JournalSection> sections) => JsonSerializer.Serialize(sections, Json);

    public static IReadOnlyList<JournalSection> Parse(string json) =>
        JsonSerializer.Deserialize<List<JournalSection>>(json, Json) ?? [];

    public static IReadOnlyList<JournalSection> Validate(IReadOnlyList<JournalSection>? sections)
    {
        if (sections is null || sections.Count == 0) throw new DomainException("A template needs at least one section.");
        if (sections.Count > MaxSections) throw new DomainException($"A template can have at most {MaxSections} sections.");

        var sectionIds = new HashSet<string>();
        var fieldIds = new HashSet<string>();
        var clean = new List<JournalSection>();
        foreach (var section in sections)
        {
            var sectionId = Id(section.Id, "Section");
            if (!sectionIds.Add(sectionId)) throw new DomainException($"Section id '{sectionId}' is used twice.");
            if (section.Fields is null || section.Fields.Count == 0)
                throw new DomainException($"Section '{section.Title}' needs at least one field.");

            var fields = new List<JournalField>();
            foreach (var field in section.Fields)
            {
                var fieldId = Id(field.Id, "Field");
                if (!fieldIds.Add(fieldId)) throw new DomainException($"Field id '{fieldId}' is used twice; ids must be unique across the template.");
                if (!Enum.IsDefined(field.Type)) throw new DomainException($"Field '{field.Label}' has an unsupported type.");

                var options = field.Options?.Select(o => o?.Trim() ?? string.Empty).ToList() ?? [];
                if (field.Type is JournalFieldType.SingleSelect or JournalFieldType.MultiSelect or JournalFieldType.Checklist)
                {
                    if (options.Count == 0) throw new DomainException($"'{field.Label}' needs at least one {(field.Type == JournalFieldType.Checklist ? "checklist item" : "option")}.");
                    if (options.Count > MaxOptions) throw new DomainException($"'{field.Label}' can have at most {MaxOptions} options.");
                    if (options.Any(o => o.Length is 0 or > 200)) throw new DomainException($"Every option of '{field.Label}' needs 1–200 characters.");
                    if (options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count) throw new DomainException($"'{field.Label}' lists an option twice.");
                }
                else options = [];

                fields.Add(new JournalField(fieldId, Text.Required(field.Label, 200, "Field label"), field.Type, field.Required,
                    Text.Optional(field.Help, 300, "Field help"), options.Count > 0 ? options : null));
            }
            clean.Add(new JournalSection(sectionId, Text.Required(section.Title, 100, "Section title"),
                Text.Optional(section.Description, 300, "Section description"), fields));
        }
        if (fieldIds.Count > MaxFields) throw new DomainException($"A template can have at most {MaxFields} fields.");
        return clean;
    }

    // Keeps only answers to fields in this version, each checked against its type. Empty answers are dropped:
    // an unanswered question is absent, never a placeholder value.
    public static Dictionary<string, JsonElement> ValidateAnswers(IReadOnlyList<JournalSection> sections, IReadOnlyDictionary<string, JsonElement>? answers)
    {
        var result = new Dictionary<string, JsonElement>();
        if (answers is null) return result;
        var fields = sections.SelectMany(s => s.Fields).ToDictionary(f => f.Id);

        foreach (var (id, value) in answers)
        {
            if (!fields.TryGetValue(id, out var field))
                throw new DomainException($"'{id}' is not a field of this template version.");
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            if (value.ValueKind == JsonValueKind.String && value.GetString()!.Trim().Length == 0) continue;
            if (field.Type == JournalFieldType.MultiSelect && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0) continue;

            result[id] = Check(field, value).Clone();
        }
        return result;
    }

    // Required questions still without an answer, by label. Checked when a trade is marked reviewed.
    public static IReadOnlyList<string> MissingRequired(IReadOnlyList<JournalSection> sections, IReadOnlyDictionary<string, JsonElement> answers) =>
        sections.SelectMany(s => s.Fields)
            // A required checkbox or checklist counts as answered once filled in, whatever was ticked:
            // an unticked box is information, not a missing answer
            .Where(f => f.Required && !answers.ContainsKey(f.Id))
            .Select(f => f.Label)
            .ToList();

    private static JsonElement Check(JournalField field, JsonElement value)
    {
        string Fail(string what) => throw new DomainException($"'{field.Label}': {what}.");
        switch (field.Type)
        {
            case JournalFieldType.ShortText:
            case JournalFieldType.LongText:
                if (value.ValueKind != JsonValueKind.String) Fail("expected text");
                var max = field.Type == JournalFieldType.ShortText ? MaxShortText : MaxLongText;
                if (value.GetString()!.Length > max) Fail($"at most {max} characters");
                return value;
            case JournalFieldType.Number:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out _)) Fail("expected a number");
                return value;
            case JournalFieldType.DateTime:
                if (value.ValueKind != JsonValueKind.String || !System.DateTime.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out _))
                    Fail("expected a date and time");
                return value;
            case JournalFieldType.SingleSelect:
                if (value.ValueKind != JsonValueKind.String || !field.Options!.Contains(value.GetString()!)) Fail("choose one of the listed options");
                return value;
            case JournalFieldType.MultiSelect:
                if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String || !field.Options!.Contains(v.GetString()!)))
                    Fail("choose from the listed options");
                return value;
            case JournalFieldType.Checkbox:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Fail("expected yes or no");
                return value;
            case JournalFieldType.Checklist:
                if (value.ValueKind != JsonValueKind.Object) Fail("expected a ticked or unticked state for each item");
                foreach (var item in value.EnumerateObject())
                    if (!field.Options!.Contains(item.Name) || item.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        Fail($"'{item.Name}' is not an item of this checklist");
                return value;
            case JournalFieldType.Rating:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var rating) || rating is < 1 or > 5) Fail("rate from 1 to 5");
                return value;
            case JournalFieldType.Url:
                if (value.ValueKind != JsonValueKind.String || !Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    Fail("expected a web address starting with http:// or https://");
                return value;
            default:
                Fail("unsupported field type");
                return value;
        }
    }

    private static string Id(string? id, string what)
    {
        id = id?.Trim() ?? string.Empty;
        if (!IdPattern().IsMatch(id))
            throw new DomainException($"{what} id '{id}' must be 1–40 lowercase letters, digits or hyphens.");
        return id;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex IdPattern();
}
