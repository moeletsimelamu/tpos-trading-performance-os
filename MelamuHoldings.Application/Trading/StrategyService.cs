using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

// A trader's strategies and their rules: create, edit (rules added, edited, reordered or retired), archive
public class StrategyService
{
    public const string NameIndex = "IX_Strategies_UserId_Name";

    private readonly IStrategyRepository _repository;
    private readonly TimeProvider _clock;

    public StrategyService(IStrategyRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<StrategyDto>> ListAsync(int userId, bool includeArchived)
    {
        var strategies = await _repository.GetAllForUserAsync(userId);
        var counts = await _repository.CountTradesAsync(userId);
        return strategies
            .Where(s => includeArchived || s.Status == StrategyStatus.Active)
            .OrderBy(s => s.Status).ThenBy(s => s.Name)
            .Select(s => ToDto(s, counts.GetValueOrDefault(s.Id)))
            .ToList();
    }

    public async Task<StrategyDto> GetAsync(int id, int userId) => await DtoAsync(await StrategyAsync(id, userId), userId);

    public async Task<StrategyDto> CreateAsync(SaveStrategyRequest request, int userId)
    {
        await EnsureNameFreeAsync(userId, request.Name, null);
        var strategy = new Strategy(userId, request.Name, request.Description, Now);
        strategy.SetRules(Drafts(request), Now);
        await _repository.AddAsync(strategy);
        await SaveAsync(request.Name);
        return ToDto(strategy, 0);
    }

    public async Task<StrategyDto> UpdateAsync(int id, SaveStrategyRequest request, int userId)
    {
        var strategy = await StrategyAsync(id, userId);
        await EnsureNameFreeAsync(userId, request.Name, id);
        strategy.Update(request.Name, request.Description, Now);
        strategy.SetRules(Drafts(request), Now);
        await SaveAsync(request.Name);
        return await DtoAsync(strategy, userId);
    }

    public Task<StrategyDto> ArchiveAsync(int id, int userId) => ChangeAsync(id, userId, s => s.Archive(Now));
    public Task<StrategyDto> RestoreAsync(int id, int userId) => ChangeAsync(id, userId, s => s.Restore(Now));
    public Task<StrategyDto> RestoreRuleAsync(int id, int ruleId, int userId) => ChangeAsync(id, userId, s => s.RestoreRule(ruleId, Now));

    private async Task<StrategyDto> ChangeAsync(int id, int userId, Action<Strategy> change)
    {
        var strategy = await StrategyAsync(id, userId);
        change(strategy);
        await _repository.SaveChangesAsync();
        return await DtoAsync(strategy, userId);
    }

    private async Task<Strategy> StrategyAsync(int id, int userId) =>
        await _repository.GetForUserAsync(id, userId) ?? throw new NotFoundException($"Strategy {id} does not exist.");

    private async Task EnsureNameFreeAsync(int userId, string name, int? id)
    {
        if (await _repository.GetByNameAsync(userId, name.Trim()) is { } other && other.Id != id)
            throw new ConflictException($"You already have a strategy named '{other.Name}'.");
    }

    private async Task SaveAsync(string name)
    {
        try
        {
            await _repository.SaveChangesAsync();
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == NameIndex)
        {
            throw new ConflictException($"You already have a strategy named '{name.Trim()}'.");
        }
    }

    private static List<RuleDraft> Drafts(SaveStrategyRequest request) =>
        request.Rules.Select(r => new RuleDraft(r.Id, r.Text, r.IsMandatory)).ToList();

    private async Task<StrategyDto> DtoAsync(Strategy strategy, int userId) =>
        ToDto(strategy, (await _repository.CountTradesAsync(userId)).GetValueOrDefault(strategy.Id));

    internal static StrategyDto ToDto(Strategy s, int tradeCount) => new(
        s.Id, s.Name, s.Description, s.Status,
        s.ActiveRules.Select(Rule).ToList(),
        s.Rules.Where(r => !r.IsActive).OrderByDescending(r => r.RetiredAt).Select(Rule).ToList(),
        tradeCount, s.CreatedAt, s.UpdatedAt);

    private static StrategyRuleDto Rule(StrategyRule r) => new(r.Id, r.Text, r.IsMandatory, r.SortOrder, r.RetiredAt);
}

// A trade's checklist: read with its history, checked before a new trade is saved, recorded after
public class TradeChecklists
{
    private const string ChecklistIndex = "IX_TradeChecklists_TradeId_StrategyId";
    private const string ResponseIndex = "IX_ChecklistResponses_ChecklistItemId_Sequence";

    private readonly IStrategyRepository _repository;

    public TradeChecklists(IStrategyRepository repository) => _repository = repository;

    // The checklist for the trade's current strategy. A checklist kept from an earlier strategy is not shown.
    public async Task<TradeChecklistDto?> GetAsync(Trade trade)
    {
        if (trade.StrategyId is not { } strategyId) return null;
        var checklist = await _repository.GetChecklistAsync(trade.Id, strategyId);
        return checklist is null ? null : ToDto(checklist, trade.Strategy?.Name ?? string.Empty);
    }

    // For a trade not saved yet: the answers must belong to the active rules of the strategy it names
    public async Task ValidateNewAsync(int userId, string? strategyName, IReadOnlyList<RuleAnswerRequest>? answers)
    {
        var parsed = Parse(answers);
        if (parsed.Count == 0) return;
        var strategy = string.IsNullOrWhiteSpace(strategyName) ? null : await _repository.GetByNameAsync(userId, strategyName.Trim());
        if (strategy is null) throw new DomainException("Choose a strategy with rules before answering its checklist.");
        TradeChecklist.Validate(strategy.ActiveRules.Select(r => (r.Id, r.IsMandatory, r.Text)).ToList(), parsed);
    }

    // Starts the checklist on the first answer (copying the strategy's active rules) and appends changed answers.
    // The caller saves.
    public async Task RecordAsync(Trade trade, IReadOnlyList<RuleAnswerRequest>? answers, DateTime now)
    {
        var parsed = Parse(answers);
        if (parsed.Count == 0) return;
        if (trade.Strategy is null) throw new DomainException("Choose a strategy with rules before answering its checklist.");

        var checklist = trade.Id == 0 ? null : await _repository.GetChecklistAsync(trade.Id, trade.Strategy.Id);
        if (checklist is null)
        {
            checklist = new TradeChecklist(trade.Id, trade.Strategy, now);
            _repository.Add(checklist);
        }
        checklist.Answer(parsed, now);
    }

    public async Task SaveAsync()
    {
        try
        {
            await _repository.SaveChangesAsync();
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName is ChecklistIndex or ResponseIndex)
        {
            throw new ConflictException("The checklist was changed by another request at the same moment. Reload the trade and try again.");
        }
    }

    private static Dictionary<int, RuleAnswer> Parse(IReadOnlyList<RuleAnswerRequest>? answers)
    {
        var parsed = new Dictionary<int, RuleAnswer>();
        foreach (var a in answers ?? [])
            if (!parsed.TryAdd(a.RuleId!.Value, a.Answer!.Value))
                throw new DomainException($"Rule {a.RuleId} is answered twice.");
        return parsed;
    }

    internal static TradeChecklistDto ToDto(TradeChecklist c, string strategyName)
    {
        var items = c.Items;
        return new TradeChecklistDto(
            c.StrategyId, strategyName, c.CreatedAt,
            items.Select(i => new ChecklistItemDto(i.StrategyRuleId, i.Text, i.IsMandatory, i.Current?.Answer, i.Current?.AnsweredAt)).ToList(),
            c.IsComplete, c.IsCompliant, c.Violations.Select(v => v.Text).ToList(),
            items.SelectMany(i => i.Responses.Select(r => new ChecklistChangeDto(i.StrategyRuleId, i.Text, r.Sequence, r.Answer, r.AnsweredAt)))
                .OrderBy(h => h.AnsweredAt).ThenBy(h => h.Sequence).ToList());
    }
}
