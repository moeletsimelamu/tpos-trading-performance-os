using System.Text.Json;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Trading.Journal;

namespace MelamuHoldings.UnitTests;

public class JournalTemplateTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static JournalSection Section(params JournalField[] fields) => new("review", "Review", null, fields);

    private static Dictionary<string, JsonElement> Answers(object values) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(values))!;

    [Fact]
    public void The_default_template_is_valid_and_carries_the_briefs_sections()
    {
        var sections = JournalSchema.Validate(DefaultJournalTemplate.Sections);

        Assert.Equal(["context", "pre-trade", "psychology", "post-trade", "learning"], sections.Select(s => s.Id));
        Assert.Equal(7, sections.Single(s => s.Id == "pre-trade").Fields.Single().Options!.Count);
    }

    [Fact]
    public void Templates_reject_duplicate_ids_bad_ids_and_choices_without_options()
    {
        Assert.Throws<DomainException>(() => JournalSchema.Validate([Section(new JournalField("a", "A", JournalFieldType.ShortText), new JournalField("a", "B", JournalFieldType.LongText))]));
        Assert.Throws<DomainException>(() => JournalSchema.Validate([Section(new JournalField("Bad Id", "A", JournalFieldType.ShortText))]));
        Assert.Throws<DomainException>(() => JournalSchema.Validate([Section(new JournalField("pick", "Pick", JournalFieldType.SingleSelect))]));
        Assert.Throws<DomainException>(() => JournalSchema.Validate([Section(new JournalField("pick", "Pick", JournalFieldType.MultiSelect, Options: ["A", "a"]))]));
        Assert.Throws<DomainException>(() => JournalSchema.Validate([]));
    }

    [Fact]
    public void Answers_are_checked_against_their_field_types()
    {
        var sections = JournalSchema.Validate([Section(
            new JournalField("valid", "Valid?", JournalFieldType.SingleSelect, Options: ["Yes", "No"]),
            new JournalField("rating", "Quality", JournalFieldType.Rating),
            new JournalField("link", "Chart", JournalFieldType.Url),
            new JournalField("checks", "Checks", JournalFieldType.Checklist, Options: ["Stop set", "Risk ok"]),
            new JournalField("tags", "Influences", JournalFieldType.MultiSelect, Options: ["FOMO", "Hesitation"]))]);

        var ok = JournalSchema.ValidateAnswers(sections, Answers(new
        {
            valid = "Yes", rating = 4, link = "https://example.com/chart.png",
            checks = new Dictionary<string, bool> { ["Stop set"] = true, ["Risk ok"] = false }, tags = new[] { "FOMO" }
        }));
        Assert.Equal(5, ok.Count);

        Assert.Throws<DomainException>(() => JournalSchema.ValidateAnswers(sections, Answers(new { valid = "Maybe" })));
        Assert.Throws<DomainException>(() => JournalSchema.ValidateAnswers(sections, Answers(new { rating = 6 })));
        Assert.Throws<DomainException>(() => JournalSchema.ValidateAnswers(sections, Answers(new { link = "ftp://example.com" })));
        Assert.Throws<DomainException>(() => JournalSchema.ValidateAnswers(sections, Answers(new { checks = new Dictionary<string, bool> { ["Not an item"] = true } })));
        Assert.Throws<DomainException>(() => JournalSchema.ValidateAnswers(sections, Answers(new { unknown = "x" })));
    }

    [Fact]
    public void Empty_answers_are_dropped_not_stored_as_placeholders()
    {
        var sections = JournalSchema.Validate([Section(new JournalField("lesson", "Lesson", JournalFieldType.ShortText, Required: true), new JournalField("tags", "Tags", JournalFieldType.MultiSelect, Options: ["A"]))]);

        var answers = JournalSchema.ValidateAnswers(sections, Answers(new { lesson = "  ", tags = Array.Empty<string>() }));

        Assert.Empty(answers);
        Assert.Equal(["Lesson"], JournalSchema.MissingRequired(sections, answers));
    }

    [Fact]
    public void Changing_the_questions_adds_a_version_and_renaming_does_not()
    {
        var template = new JournalTemplate(1, "Mine", null, [Section(new JournalField("lesson", "Lesson", JournalFieldType.ShortText))], Now);

        Assert.False(template.Update("Renamed", "Now with a description", [Section(new JournalField("lesson", "Lesson", JournalFieldType.ShortText))], Now));
        Assert.Equal(1, template.Current.Version);

        Assert.True(template.Update("Renamed", null, [Section(new JournalField("lesson", "Key lesson", JournalFieldType.LongText), new JournalField("mood", "Mood", JournalFieldType.Rating))], Now));
        Assert.Equal(2, template.Current.Version);
        Assert.Equal(["Lesson"], template.Versions.Single(v => v.Version == 1).Sections.Single().Fields.Select(f => f.Label));
    }

    [Fact]
    public void An_entry_keeps_the_answers_and_questions_of_its_version()
    {
        var template = new JournalTemplate(1, "Mine", null, [Section(new JournalField("lesson", "Lesson", JournalFieldType.ShortText))], Now);
        var v1 = template.Current;
        var entry = new TradeJournalEntry(7, v1, Answers(new { lesson = "Waited for confirmation" }), Now);

        template.Update("Mine", null, [Section(new JournalField("mood", "Mood", JournalFieldType.Rating))], Now);

        // The v1 answer stays readable against v1, even though v2 no longer has the question
        Assert.Equal("Waited for confirmation", entry.AnswerValues["lesson"].GetString());
        Assert.Equal("lesson", v1.Sections.Single().Fields.Single().Id);
        // ...and it cannot be saved against v2, which has no such question
        Assert.Throws<DomainException>(() => entry.Record(template.Current, entry.AnswerValues, Now));
    }

    [Fact]
    public void The_default_cannot_be_archived_and_an_archived_template_cannot_become_default()
    {
        var template = new JournalTemplate(1, "Mine", null, DefaultJournalTemplate.Sections, Now);
        template.MakeDefault(Now);
        Assert.Throws<DomainException>(() => template.Archive(Now));

        var other = new JournalTemplate(1, "Other", null, DefaultJournalTemplate.Sections, Now);
        other.Archive(Now);
        Assert.Throws<DomainException>(() => other.MakeDefault(Now));
    }
}
