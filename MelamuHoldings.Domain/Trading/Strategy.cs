using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Trading;

public enum StrategyStatus { Active, Archived }

// What part of the trade a rule is about; General for a rule that is not about one stage
public enum RuleCategory { General, Setup, Entry, Exit, Risk }

// A trader's own strategy and its rules. A rule is mandatory (breaking it is a violation) or optional
// (informational). Rules leave the list by being retired, never deleted, so checklists keep their meaning.
public class Strategy
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;
    public const int MaxActiveRules = 30;
    public const int MaxScopeItems = 20;
    public const int MaxScopeItemLength = 50;

    private readonly List<StrategyRule> _rules = new();

    public int Id { get; private set; }
    public int UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public StrategyStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Where the strategy is meant to be traded. Empty means anywhere; a trade outside them is noted, never refused.
    public List<string> Instruments { get; private set; } = [];
    public List<string> Sessions { get; private set; } = [];

    // The journal template a new trade under this strategy starts with (the trader's own; checked by the service)
    public int? DefaultTemplateId { get; private set; }

    public IReadOnlyList<StrategyRule> Rules => _rules;
    public IReadOnlyList<StrategyRule> ActiveRules => _rules.Where(r => r.IsActive).OrderBy(r => r.SortOrder).ToList();

    // Navigation properties
    public ICollection<Trade> Trades { get; private set; } = new List<Trade>();

    private Strategy() { }

    public Strategy(int userId, string name, string? description, DateTime now)
    {
        UserId = userId;
        SetDetails(name, description);
        Status = StrategyStatus.Active;
        CreatedAt = now;
    }

    public void Update(string name, string? description, DateTime now)
    {
        SetDetails(name, description);
        UpdatedAt = now;
    }

    // The active rules become exactly the given list, in its order. A rule with an id is edited in place
    // (checklists keep the wording they were answered with); one without is added; an active rule left out
    // is retired and can be restored later.
    public void SetRules(IReadOnlyList<RuleDraft> drafts, DateTime now)
    {
        if (drafts.Count > MaxActiveRules) throw new DomainException($"A strategy can have at most {MaxActiveRules} active rules.");

        var texts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new HashSet<StrategyRule>();
        var order = 0;
        foreach (var draft in drafts)
        {
            var text = Text.Required(draft.Text, StrategyRule.MaxTextLength, "Rule");
            if (!texts.Add(text)) throw new DomainException($"The rule '{text}' is listed twice.");

            StrategyRule rule;
            if (draft.Id is { } id)
            {
                rule = _rules.FirstOrDefault(r => r.Id == id && id != 0) ?? throw new DomainException($"Rule {id} is not part of this strategy.");
                rule.Edit(text, draft.IsMandatory, draft.Category, ++order);
                rule.Reactivate();
            }
            else
            {
                rule = new StrategyRule(text, draft.IsMandatory, draft.Category, ++order, now);
                _rules.Add(rule);
            }
            kept.Add(rule);
        }

        foreach (var rule in _rules.Where(r => r.IsActive && !kept.Contains(r)))
            rule.Retire(now);
        UpdatedAt = now;
    }

    // A retired rule comes back at the end of the list
    public void RestoreRule(int ruleId, DateTime now)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == ruleId) ?? throw new DomainException($"Rule {ruleId} is not part of this strategy.");
        if (rule.IsActive) return;
        var active = ActiveRules;
        if (active.Count >= MaxActiveRules) throw new DomainException($"A strategy can have at most {MaxActiveRules} active rules.");
        if (active.Any(r => string.Equals(r.Text, rule.Text, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException($"An active rule already reads '{rule.Text}'.");
        rule.Edit(rule.Text, rule.IsMandatory, rule.Category, active.Count == 0 ? 1 : active.Max(r => r.SortOrder) + 1);
        rule.Reactivate();
        UpdatedAt = now;
    }

    // Instruments and sessions are kept as entered, trimmed, once each (ignoring case)
    public void SetScope(IEnumerable<string>? instruments, IEnumerable<string>? sessions, DateTime now)
    {
        Instruments = Scope(instruments, "instrument");
        Sessions = Scope(sessions, "session");
        UpdatedAt = now;
    }

    public void SetDefaultTemplate(int? templateId, DateTime now)
    {
        DefaultTemplateId = templateId;
        UpdatedAt = now;
    }

    // Whether a trade's instrument and session are within the strategy's scope; an empty list allows anything
    public bool CoversInstrument(string? instrument) =>
        Instruments.Count == 0 || Instruments.Any(i => string.Equals(i, instrument?.Trim(), StringComparison.OrdinalIgnoreCase));

    public bool CoversSession(string? session) =>
        Sessions.Count == 0 || Sessions.Any(s => string.Equals(s, session?.Trim(), StringComparison.OrdinalIgnoreCase));

    public void Archive(DateTime now)
    {
        Status = StrategyStatus.Archived;
        UpdatedAt = now;
    }

    public void Restore(DateTime now)
    {
        Status = StrategyStatus.Active;
        UpdatedAt = now;
    }

    private void SetDetails(string name, string? description)
    {
        Name = Text.Required(name, MaxNameLength, "Strategy name");
        Description = Text.Optional(description, MaxDescriptionLength, "Strategy description");
    }

    private static List<string> Scope(IEnumerable<string>? values, string what)
    {
        var kept = new List<string>();
        foreach (var value in values ?? [])
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            if (text.Length > MaxScopeItemLength) throw new DomainException($"A {what} can be at most {MaxScopeItemLength} characters.");
            if (!kept.Contains(text, StringComparer.OrdinalIgnoreCase)) kept.Add(text);
        }
        if (kept.Count > MaxScopeItems) throw new DomainException($"A strategy can list at most {MaxScopeItems} {what}s.");
        return kept;
    }
}

public record RuleDraft(int? Id, string Text, bool IsMandatory, RuleCategory Category = RuleCategory.General);

public class StrategyRule
{
    public const int MaxTextLength = 200;

    public int Id { get; private set; }
    public int StrategyId { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public bool IsMandatory { get; private set; }
    public RuleCategory Category { get; private set; }
    public int SortOrder { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RetiredAt { get; private set; }

    public bool IsActive => RetiredAt is null;

    private StrategyRule() { }

    internal StrategyRule(string text, bool isMandatory, RuleCategory category, int sortOrder, DateTime now)
    {
        Text = text;
        IsMandatory = isMandatory;
        Category = Defined(category);
        SortOrder = sortOrder;
        CreatedAt = now;
    }

    internal void Edit(string text, bool isMandatory, RuleCategory category, int sortOrder)
    {
        Text = text;
        IsMandatory = isMandatory;
        Category = Defined(category);
        SortOrder = sortOrder;
    }

    private static RuleCategory Defined(RuleCategory category) =>
        Enum.IsDefined(category) ? category : throw new DomainException("Choose a rule category: setup, entry, exit, risk or general.");

    internal void Retire(DateTime now) => RetiredAt ??= now;
    internal void Reactivate() => RetiredAt = null;
}
