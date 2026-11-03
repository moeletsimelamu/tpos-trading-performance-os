using System.ComponentModel.DataAnnotations;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Application.Finance;
using MelamuHoldings.Domain.Finance;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public class PropAccountRulesRequest
{
    [Required] public DrawdownModel? DrawdownModel { get; set; }
    public decimal? MaxDrawdown { get; set; }
    public bool TrailingLocksAtStart { get; set; }
    public decimal? DailyLossLimit { get; set; }
    public decimal? ProfitTarget { get; set; }
    public int? MinimumTradingDays { get; set; }
    public decimal? PayoutSplitPercent { get; set; }
    public decimal? PurchaseFee { get; set; }
    public decimal? ResetFee { get; set; }
    public decimal? MonthlyFee { get; set; }
    [MaxLength(PropAccountRules.MaxNotesLength)] public string? Notes { get; set; }

    internal PropAccountRules ToRules() => new(
        DrawdownModel!.Value, MaxDrawdown, TrailingLocksAtStart, DailyLossLimit, ProfitTarget, MinimumTradingDays, PayoutSplitPercent, PurchaseFee, ResetFee, MonthlyFee, Notes);
}

public class CreatePropAccountRequest
{
    [Required, MaxLength(PropFirmAccount.MaxFirmLength)] public string Firm { get; set; } = string.Empty;
    [Required, MaxLength(PropFirmAccount.MaxNicknameLength)] public string Nickname { get; set; } = string.Empty;
    [Required] public decimal? AccountSize { get; set; }
    [Required] public string Currency { get; set; } = string.Empty;
    [Required] public PropAccountPhase? Phase { get; set; }
    [MaxLength(PropFirmAccount.MaxLoginLength)] public string? Login { get; set; }
    [Required] public DateOnly? OpenedOn { get; set; }
    [Required] public PropAccountRulesRequest? Rules { get; set; }
    [MaxLength(PropFirmAccount.MaxNotesLength)] public string? Notes { get; set; }
}

public class UpdatePropAccountRequest
{
    [Required, MaxLength(PropFirmAccount.MaxFirmLength)] public string Firm { get; set; } = string.Empty;
    [Required, MaxLength(PropFirmAccount.MaxNicknameLength)] public string Nickname { get; set; } = string.Empty;
    [Required] public decimal? AccountSize { get; set; }
    [MaxLength(PropFirmAccount.MaxLoginLength)] public string? Login { get; set; }
    [MaxLength(PropFirmAccount.MaxNotesLength)] public string? Notes { get; set; }
}

public class ChangePropAccountRulesRequest
{
    [Required] public DateOnly? EffectiveFrom { get; set; }
    [Required] public PropAccountRulesRequest? Rules { get; set; }
}

public class MovePropAccountPhaseRequest
{
    [Required] public PropAccountPhase? Phase { get; set; }
    [Required] public DateOnly? Date { get; set; }
    public decimal? StartingBalance { get; set; }
    [MaxLength(PropFirmAccount.MaxLoginLength)] public string? Login { get; set; }
    [Required] public PropAccountRulesRequest? Rules { get; set; }
    [MaxLength(PropAccountEvent.MaxNoteLength)] public string? Reason { get; set; }
}

public class ChangePropAccountStatusRequest
{
    [Required] public PropAccountStatus? Status { get; set; }
    [Required] public DateOnly? Date { get; set; }
    [MaxLength(PropAccountEvent.MaxNoteLength)] public string? Reason { get; set; }
}

public class ResetPropAccountRequest
{
    [Required] public DateOnly? Date { get; set; }
    public decimal? StartingBalance { get; set; }
    [MaxLength(PropAccountEvent.MaxNoteLength)] public string? Note { get; set; }
}

public class RecordPropAccountBalanceRequest
{
    [Required] public DateOnly? AsOf { get; set; }
    [Required] public decimal? Balance { get; set; }
    public decimal? DrawdownLevel { get; set; }
    [MaxLength(PropAccountEvent.MaxNoteLength)] public string? Note { get; set; }
}

public record PropAccountRuleSetDto(int Version, PropAccountPhase Phase, DateOnly EffectiveFrom, PropAccountRules Rules, DateTime CreatedAt);

public record PropAccountEventDto(
    int Id, PropAccountEventKind Kind, DateOnly OccurredOn, PropAccountPhase Phase, PropAccountStatus Status, string? Login,
    MoneyDto? StartingBalance, string? Note, DateTime RecordedAt);

// Expected and Difference: what the recorded trades came to on that date, for a reading in the current run
public record PropAccountBalanceDto(int Id, DateOnly AsOf, MoneyDto Balance, MoneyDto? DrawdownLevel, string? Note, decimal? Expected, decimal? Difference, DateTime RecordedAt);

// The app's figures for the current run. OpenTrades and TradesBeforeRun are linked trades not counted in them.
public record PropAccountStandingDto(PropAccountStanding Figures, int LinkedTrades, int OpenTrades, int TradesBeforeRun);

// History and earlier rule versions are newest first
public record PropAccountDto(
    int Id, string Firm, string Nickname, MoneyDto AccountSize, PropAccountPhase Phase, PropAccountStatus Status, string? Login, string? Notes,
    DateOnly OpenedOn, DateOnly RunStartedOn, MoneyDto StartingBalance, PropAccountRuleSetDto Rules,
    IReadOnlyList<PropAccountRuleSetDto> RuleHistory, IReadOnlyList<PropAccountEventDto> History,
    PropAccountStandingDto Standing, IReadOnlyList<PropAccountBalanceDto> Balances, DateTime CreatedAt, DateTime? UpdatedAt);

// A trader's prop firm accounts: what they are, the rules that apply and how they got here. Nothing here posts to the ledger.
public class PropAccountService
{
    public const string ActiveNicknameIndex = "IX_PropFirmAccounts_UserId_Nickname_Open";
    public const string BalanceDayIndex = "IX_PropAccountBalances_PropFirmAccountId_AsOf";

    private readonly IPropAccountRepository _repository;
    private readonly TimeProvider _clock;

    public PropAccountService(IPropAccountRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    // Live accounts first; closed ones only when asked for
    public async Task<IReadOnlyList<PropAccountDto>> ListAsync(int userId, bool includeClosed)
    {
        var trades = (await _repository.GetLinkedTradesAsync(userId)).ToLookup(t => t.AccountId);
        return (await _repository.GetAllForUserAsync(userId))
            .Where(a => includeClosed || a.Status != PropAccountStatus.Closed)
            .OrderBy(a => a.Status).ThenBy(a => a.Nickname)
            .Select(a => ToDto(a, trades[a.Id]))
            .ToList();
    }

    public async Task<PropAccountDto> GetAsync(int id, int userId) => await DtoAsync(await AccountAsync(id, userId), userId);

    public async Task<PropAccountDto> CreateAsync(CreatePropAccountRequest request, int userId)
    {
        var size = Money.Of(request.AccountSize!.Value, Currency.FromCode(request.Currency));
        var account = new PropFirmAccount(userId, request.Firm, request.Nickname, size, request.Phase!.Value, request.Login, request.OpenedOn!.Value, request.Rules!.ToRules(), request.Notes, Now);
        _repository.Add(account);
        await SaveAsync(account);
        return ToDto(account, []);
    }

    public Task<PropAccountDto> UpdateAsync(int id, UpdatePropAccountRequest request, int userId) =>
        ChangeAsync(id, userId, a => a.UpdateDetails(request.Firm, request.Nickname, request.Login, Money.Of(request.AccountSize!.Value, a.Currency), request.Notes, Now));

    public Task<PropAccountDto> ChangeRulesAsync(int id, ChangePropAccountRulesRequest request, int userId) =>
        ChangeAsync(id, userId, a => a.ChangeRules(request.Rules!.ToRules(), request.EffectiveFrom!.Value, Now));

    public Task<PropAccountDto> MoveToPhaseAsync(int id, MovePropAccountPhaseRequest request, int userId) =>
        ChangeAsync(id, userId, a => a.MoveToPhase(request.Phase!.Value, request.Date!.Value, Start(request.StartingBalance, a), request.Login, request.Rules!.ToRules(), request.Reason, Now));

    public Task<PropAccountDto> ChangeStatusAsync(int id, ChangePropAccountStatusRequest request, int userId) =>
        ChangeAsync(id, userId, a => a.ChangeStatus(request.Status!.Value, request.Date!.Value, request.Reason, Now));

    public Task<PropAccountDto> ResetAsync(int id, ResetPropAccountRequest request, int userId) =>
        ChangeAsync(id, userId, a => a.Reset(request.Date!.Value, Start(request.StartingBalance, a), request.Note, Now));

    public Task<PropAccountDto> RecordBalanceAsync(int id, RecordPropAccountBalanceRequest request, int userId) =>
        ChangeAsync(id, userId, a => a.RecordBalance(request.AsOf!.Value, Money.Of(request.Balance!.Value, a.Currency), Start(request.DrawdownLevel, a), request.Note, Now));

    public Task<PropAccountDto> RemoveBalanceAsync(int id, int balanceId, int userId) =>
        ChangeAsync(id, userId, a => a.RemoveBalance(balanceId, Now));

    private async Task<PropAccountDto> ChangeAsync(int id, int userId, Action<PropFirmAccount> change)
    {
        var account = await AccountAsync(id, userId);
        change(account);
        await SaveAsync(account);
        return await DtoAsync(account, userId);
    }

    private async Task<PropAccountDto> DtoAsync(PropFirmAccount account, int userId) =>
        ToDto(account, (await _repository.GetLinkedTradesAsync(userId)).Where(t => t.AccountId == account.Id));

    private async Task<PropFirmAccount> AccountAsync(int id, int userId) =>
        await _repository.GetForUserAsync(id, userId) ?? throw new NotFoundException($"Prop firm account {id} does not exist.");

    private async Task SaveAsync(PropFirmAccount account)
    {
        try
        {
            await _repository.SaveChangesAsync();
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == ActiveNicknameIndex)
        {
            throw new ConflictException($"You already have an open account nicknamed '{account.Nickname}'.");
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == BalanceDayIndex)
        {
            throw new ConflictException("A balance for that date was recorded by another request at the same time. Reload the account.");
        }
    }

    private static Money? Start(decimal? amount, PropFirmAccount account) => amount is { } value ? Money.Of(value, account.Currency) : null;

    private static PropAccountRuleSetDto ToDto(PropAccountRuleSet r) => new(r.Version, r.Phase, r.EffectiveFrom, r.Rules, r.CreatedAt);

    // A trade counts once it is closed, on the day it closed. Its P&L is net as the platform reports it, so fees are not taken off again.
    private PropAccountStanding Figures(PropFirmAccount a, IReadOnlyList<LinkedTrade> trades) => PropAccountStanding.Calculate(
        a.CurrentRules.Rules, a.StartingBalance, a.RunStartedOn,
        trades.Where(Closed).Select(t => new ClosedTradeResult(t.TradeId, ClosedOn(t), t.ExitTime ?? t.EntryTime, t.PnL!.Value)),
        a.Balances.Select(b => new BalanceReading(b.AsOf, b.Amount, b.DrawdownLevel)),
        DateOnly.FromDateTime(Now));

    private static bool Closed(LinkedTrade t) => t.Status is TradeStatus.Closed or TradeStatus.Reviewed && t.PnL is not null;
    private static DateOnly ClosedOn(LinkedTrade t) => t.ExitDate ?? t.TradeDate;

    private PropAccountDto ToDto(PropFirmAccount a, IEnumerable<LinkedTrade> linked)
    {
        var code = a.Currency.Code;
        var trades = linked.ToList();
        var figures = Figures(a, trades);
        var standing = new PropAccountStandingDto(
            figures, trades.Count, trades.Count(t => t.Status == TradeStatus.Open), trades.Count(t => Closed(t) && ClosedOn(t) < a.RunStartedOn));
        return new PropAccountDto(
            a.Id, a.Firm, a.Nickname, new MoneyDto(a.AccountSize, code), a.Phase, a.Status, a.Login, a.Notes,
            a.OpenedOn, a.RunStartedOn, new MoneyDto(a.StartingBalance, code), ToDto(a.CurrentRules),
            a.RuleSets.OrderByDescending(r => r.Version).Select(ToDto).ToList(),
            a.Events.OrderByDescending(e => e.Id).Select(e => new PropAccountEventDto(
                e.Id, e.Kind, e.OccurredOn, e.Phase, e.Status, e.Login, e.StartingBalance is { } s ? new MoneyDto(s, code) : null, e.Note, e.RecordedAt)).ToList(),
            standing,
            a.Balances.OrderByDescending(b => b.AsOf).Select(b =>
            {
                var check = figures.Readings.FirstOrDefault(r => r.AsOf == b.AsOf);
                return new PropAccountBalanceDto(
                    b.Id, b.AsOf, new MoneyDto(b.Amount, code), b.DrawdownLevel is { } level ? new MoneyDto(level, code) : null, b.Note, check?.Expected, check?.Difference, b.RecordedAt);
            }).ToList(),
            a.CreatedAt, a.UpdatedAt);
    }
}
