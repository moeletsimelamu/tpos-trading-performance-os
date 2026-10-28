using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Trading;

public enum RuleAnswer { Met, NotMet, NotApplicable }

// A trade's answers to its strategy's rules. The rules are copied when the checklist starts, so editing or
// retiring a rule later never changes what a past checklist asked or how it scored. Answers are appended,
// never overwritten: the latest answer per rule counts, and every earlier one stays as history.
public class TradeChecklist
{
    private readonly List<ChecklistItem> _items = new();

    public int Id { get; private set; }
    public int TradeId { get; private set; }
    public int StrategyId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public IReadOnlyList<ChecklistItem> Items => _items.OrderBy(i => i.SortOrder).ToList();

    // Every rule answered
    public bool IsComplete => _items.Count > 0 && _items.All(i => i.Current is not null);

    // Only a mandatory rule that was not met is a violation; optional rules are informational
    public IReadOnlyList<ChecklistItem> Violations =>
        Items.Where(i => i.IsMandatory && i.Current?.Answer == RuleAnswer.NotMet).ToList();

    // Known only once the checklist is complete
    public bool? IsCompliant => IsComplete ? Violations.Count == 0 : null;

    private TradeChecklist() { }

    public TradeChecklist(int tradeId, Strategy strategy, DateTime now)
    {
        var rules = strategy.ActiveRules;
        if (rules.Count == 0) throw new DomainException($"Strategy '{strategy.Name}' has no rules to check.");
        TradeId = tradeId;
        StrategyId = strategy.Id;
        CreatedAt = now;
        _items.AddRange(rules.Select(r => new ChecklistItem(r)));
    }

    // Checks answers without recording them, so a new trade is never saved with a checklist that will be refused
    public static void Validate(IReadOnlyList<(int RuleId, bool IsMandatory, string Text)> rules, IReadOnlyDictionary<int, RuleAnswer> answers)
    {
        foreach (var (ruleId, answer) in answers)
        {
            var rule = rules.FirstOrDefault(r => r.RuleId == ruleId);
            if (rule == default) throw new DomainException($"Rule {ruleId} is not on this trade's checklist.");
            if (rule.IsMandatory && answer == RuleAnswer.NotApplicable)
                throw new DomainException($"'{rule.Text}' is mandatory: it was either met or not met.");
        }
    }

    public IReadOnlyList<(int RuleId, bool IsMandatory, string Text)> Rules =>
        Items.Select(i => (i.StrategyRuleId, i.IsMandatory, i.Text)).ToList();

    // Records the answers that differ from the current ones; returns how many changed
    public int Answer(IReadOnlyDictionary<int, RuleAnswer> answers, DateTime now)
    {
        Validate(Rules, answers);
        var changed = 0;
        foreach (var (ruleId, answer) in answers)
        {
            var item = _items.Single(i => i.StrategyRuleId == ruleId);
            if (item.Current?.Answer == answer) continue;
            item.Record(answer, now);
            changed++;
        }
        return changed;
    }
}

// One rule on one trade's checklist, with the wording and weight it had when the checklist started
public class ChecklistItem
{
    private readonly List<ChecklistResponse> _responses = new();

    public int Id { get; private set; }
    public int TradeChecklistId { get; private set; }
    public int StrategyRuleId { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public bool IsMandatory { get; private set; }
    public int SortOrder { get; private set; }

    public IReadOnlyList<ChecklistResponse> Responses => _responses.OrderBy(r => r.Sequence).ToList();
    public ChecklistResponse? Current => _responses.MaxBy(r => r.Sequence);

    private ChecklistItem() { }

    internal ChecklistItem(StrategyRule rule)
    {
        StrategyRuleId = rule.Id;
        Text = rule.Text;
        IsMandatory = rule.IsMandatory;
        SortOrder = rule.SortOrder;
    }

    internal void Record(RuleAnswer answer, DateTime now) =>
        _responses.Add(new ChecklistResponse(_responses.Count + 1, answer, now));
}

// An answer as given at one moment. Sequence 1 is the first answer; later ones are changes.
public class ChecklistResponse
{
    public int Id { get; private set; }
    public int ChecklistItemId { get; private set; }
    public int Sequence { get; private set; }
    public RuleAnswer Answer { get; private set; }
    public DateTime AnsweredAt { get; private set; }

    private ChecklistResponse() { }

    internal ChecklistResponse(int sequence, RuleAnswer answer, DateTime now)
    {
        Sequence = sequence;
        Answer = answer;
        AnsweredAt = now;
    }
}
