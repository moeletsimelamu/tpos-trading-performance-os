using System.Globalization;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Domain.Trading;

// The order matters: moving to an earlier phase is a correction and needs a reason
public enum PropAccountPhase { Evaluation, Verification, Funded }

public enum PropAccountStatus { Active, Pending, Breached, Closed }

public enum PropAccountEventKind { Opened, PhaseChanged, StatusChanged, Reset, RulesChanged, DetailsChanged }

// How the firm measures the drawdown limit. Trailing intraday follows unrealised profit, which closed trades cannot show.
public enum DrawdownModel { None, Static, TrailingEndOfDay, TrailingIntraday }

// What a firm sets for an account, in the account's currency. Each limit is optional: empty means this firm has none.
// The fees are the firm's stated prices, for reference; fees actually paid are expenses in the ledger.
public sealed record PropAccountRules(
    DrawdownModel DrawdownModel, decimal? MaxDrawdown, bool TrailingLocksAtStart, decimal? DailyLossLimit, decimal? ProfitTarget,
    int? MinimumTradingDays, decimal? PayoutSplitPercent, decimal? PurchaseFee, decimal? ResetFee, decimal? MonthlyFee, string? Notes)
{
    public const int MaxNotesLength = 500;
    public const int MaxTradingDays = 1000;

    private bool IsTrailing => DrawdownModel is DrawdownModel.TrailingEndOfDay or DrawdownModel.TrailingIntraday;

    // The rules as they will be kept, or the reason they cannot be: limits have to fit inside the balance they apply to
    public PropAccountRules Validated(Currency currency, decimal startingBalance)
    {
        if (!Enum.IsDefined(DrawdownModel)) throw new DomainException("Choose a drawdown model: none, static, trailing end of day or trailing intraday.");
        if (DrawdownModel == DrawdownModel.None && MaxDrawdown is not null) throw new DomainException("Choose how the firm measures the drawdown limit.");
        if (DrawdownModel != DrawdownModel.None && MaxDrawdown is null) throw new DomainException("Give the drawdown limit, or choose no drawdown limit.");

        Amount(MaxDrawdown, currency, "drawdown limit");
        Amount(DailyLossLimit, currency, "daily loss limit");
        Amount(ProfitTarget, currency, "profit target");
        Amount(PurchaseFee, currency, "purchase fee");
        Amount(ResetFee, currency, "reset fee");
        Amount(MonthlyFee, currency, "monthly fee");
        if (MaxDrawdown >= startingBalance) throw new DomainException("The drawdown limit must be less than the starting balance.");
        if (DailyLossLimit >= startingBalance) throw new DomainException("The daily loss limit must be less than the starting balance.");
        if (MinimumTradingDays is < 1 or > MaxTradingDays) throw new DomainException($"The minimum trading days must be between 1 and {MaxTradingDays}.");
        if (PayoutSplitPercent is <= 0 or > 100) throw new DomainException("Your payout split must be more than 0% and at most 100%.");
        if (PayoutSplitPercent is { } split && decimal.Round(split, 2) != split) throw new DomainException("Your payout split can have at most two decimal places.");

        return this with { TrailingLocksAtStart = IsTrailing && TrailingLocksAtStart, Notes = Text.Optional(Notes, MaxNotesLength, "Rule notes") };
    }

    private static void Amount(decimal? amount, Currency currency, string what)
    {
        if (amount is not { } value) return;
        if (value <= 0) throw new DomainException($"The {what} must be more than zero.");
        Money.Of(value, currency);
    }
}

// One attempt at a prop firm account, from buying it until it ends (owner decision D10). Evaluation, verification and
// funded are phases of the same record, so its fees and payouts stay together. A paid reset stays on the record too;
// a new challenge after a breach is a new record. Each phase and reset restarts the balance. Every change is appended
// to the history, and rules change by adding a version, so what applied at any date can be read back.
public class PropFirmAccount
{
    public const int MaxFirmLength = 100;
    public const int MaxNicknameLength = 100;
    public const int MaxLoginLength = 50;
    public const int MaxNotesLength = 500;

    private readonly List<PropAccountEvent> _events = new();
    private readonly List<PropAccountRuleSet> _ruleSets = new();

    public int Id { get; private set; }
    public int UserId { get; private set; }
    public string Firm { get; private set; } = string.Empty;
    public string Nickname { get; private set; } = string.Empty;
    public Currency Currency { get; private set; } = Currency.USD;
    public decimal AccountSize { get; private set; }
    public PropAccountPhase Phase { get; private set; }
    public PropAccountStatus Status { get; private set; }

    // The firm's account number for the current phase. Never a password.
    public string? Login { get; private set; }
    public string? Notes { get; private set; }
    public DateOnly OpenedOn { get; private set; }

    // Where the balance last restarted: when the account opened, or the latest phase change or reset
    public DateOnly RunStartedOn { get; private set; }
    public decimal StartingBalance { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<PropAccountEvent> Events => _events.OrderBy(e => e.Id).ToList();
    public IReadOnlyList<PropAccountRuleSet> RuleSets => _ruleSets.OrderBy(r => r.Version).ToList();
    public PropAccountRuleSet CurrentRules => _ruleSets.MaxBy(r => r.Version)!;

    private PropFirmAccount() { }

    public PropFirmAccount(
        int userId, string firm, string nickname, Money size, PropAccountPhase phase, string? login, DateOnly openedOn,
        PropAccountRules rules, string? notes, DateTime now)
    {
        if (!size.IsPositive) throw new DomainException("The account size must be more than zero.");
        if (!Enum.IsDefined(phase)) throw new DomainException("Choose a phase: evaluation, verification or funded.");
        RequireDate(openedOn, now, "opening");

        UserId = userId;
        Currency = size.Currency;
        AccountSize = size.Amount;
        SetDetails(firm, nickname, login, notes);
        Phase = phase;
        Status = PropAccountStatus.Active;
        OpenedOn = openedOn;
        RunStartedOn = openedOn;
        StartingBalance = size.Amount;
        CreatedAt = now;
        AddRules(rules, openedOn, now);
        Append(PropAccountEventKind.Opened, openedOn, StartingBalance, null, now);
    }

    // Corrects what was entered. The currency is fixed. A changed size also corrects the current starting balance,
    // since a size only changes because it was entered wrongly; a firm's scale-up arrives as a phase change or reset.
    public void UpdateDetails(string firm, string nickname, string? login, Money size, string? notes, DateTime now)
    {
        RequireOpen();
        if (size.Currency != Currency) throw new DomainException($"The account is in {Currency.Code}; its currency cannot change.");
        if (!size.IsPositive) throw new DomainException("The account size must be more than zero.");

        var changes = new List<string>();
        var (oldFirm, oldNickname, oldLogin, oldNotes) = (Firm, Nickname, Login, Notes);
        SetDetails(firm, nickname, login, notes);
        Note(changes, "Firm", oldFirm, Firm);
        Note(changes, "Nickname", oldNickname, Nickname);
        Note(changes, "Login", oldLogin, Login);
        Note(changes, "Notes", oldNotes, Notes);

        decimal? restarted = null;
        if (size.Amount != AccountSize)
        {
            CurrentRules.Rules.Validated(Currency, size.Amount);
            changes.Add($"Size: {Format(AccountSize)} to {Format(size.Amount)}");
            AccountSize = size.Amount;
            StartingBalance = size.Amount;
            restarted = size.Amount;
        }

        if (changes.Count == 0) return;
        Append(PropAccountEventKind.DetailsChanged, DateOnly.FromDateTime(now), restarted, string.Join("; ", changes), now);
    }

    // Adds a version of the rules from a date; unchanged rules add nothing
    public void ChangeRules(PropAccountRules rules, DateOnly effectiveFrom, DateTime now)
    {
        RequireOpen();
        RequireDate(effectiveFrom, now, "rules");
        var earliest = RunStartedOn > CurrentRules.EffectiveFrom ? RunStartedOn : CurrentRules.EffectiveFrom;
        if (effectiveFrom < earliest)
            throw new DomainException($"New rules cannot apply before {earliest:yyyy-MM-dd}, when the current ones or this phase began.");
        if (rules.Validated(Currency, StartingBalance) == CurrentRules.Rules) return;

        AddRules(rules, effectiveFrom, now);
        Append(PropAccountEventKind.RulesChanged, effectiveFrom, null, $"Rules version {CurrentRules.Version}", now);
    }

    // Passing a phase: same record, the next phase's rules, and the balance restarts. The login stays unless a new one is given.
    public void MoveToPhase(PropAccountPhase phase, DateOnly on, Money? startingBalance, string? login, PropAccountRules rules, string? reason, DateTime now)
    {
        if (!Enum.IsDefined(phase)) throw new DomainException("Choose a phase: evaluation, verification or funded.");
        if (Status is PropAccountStatus.Breached or PropAccountStatus.Closed)
            throw new DomainException($"A {Status.ToString().ToLowerInvariant()} account cannot change phase.");
        if (phase == Phase) throw new DomainException($"The account is already in {Phase.ToString().ToLowerInvariant()}.");
        var note = Text.Optional(reason, PropAccountEvent.MaxNoteLength, "Reason");
        if (phase < Phase && note is null) throw new DomainException("Going back to an earlier phase is a correction: give the reason.");
        RequireRestartDate(on, now, "phase change");
        var start = StartFrom(startingBalance);

        Phase = phase;
        Status = PropAccountStatus.Active;
        Login = Text.Optional(login, MaxLoginLength, "Login") ?? Login;
        RunStartedOn = on;
        StartingBalance = start;
        AddRules(rules, on, now);
        Append(PropAccountEventKind.PhaseChanged, on, start, note, now);
    }

    // A breach always says why, and so does undoing one or reopening a closed account
    public void ChangeStatus(PropAccountStatus status, DateOnly on, string? reason, DateTime now)
    {
        if (!Enum.IsDefined(status)) throw new DomainException("Choose a status: active, pending, breached or closed.");
        if (status == Status) throw new DomainException($"The account is already {Status.ToString().ToLowerInvariant()}.");
        var note = Text.Optional(reason, PropAccountEvent.MaxNoteLength, "Reason");
        if (note is null && status == PropAccountStatus.Breached) throw new DomainException("Say which rule was breached.");
        if (note is null && Status is PropAccountStatus.Breached or PropAccountStatus.Closed)
            throw new DomainException($"Reopening a {Status.ToString().ToLowerInvariant()} account needs a reason.");
        RequireDate(on, now, "status");
        var last = LastLifecycleDate;
        if (on < last) throw new DomainException($"The status date cannot be before the last change ({last:yyyy-MM-dd}).");

        Status = status;
        Append(PropAccountEventKind.StatusChanged, on, null, note, now);
    }

    // A reset bought from the firm: same record and phase, the account is live again and the balance restarts
    public void Reset(DateOnly on, Money? startingBalance, string? note, DateTime now)
    {
        RequireOpen();
        RequireRestartDate(on, now, "reset");
        var start = StartFrom(startingBalance);
        var text = Text.Optional(note, PropAccountEvent.MaxNoteLength, "Note");
        CurrentRules.Rules.Validated(Currency, start);

        Status = PropAccountStatus.Active;
        RunStartedOn = on;
        StartingBalance = start;
        Append(PropAccountEventKind.Reset, on, start, text, now);
    }

    private DateOnly LastLifecycleDate => _events.Where(e => e.Kind is not (PropAccountEventKind.RulesChanged or PropAccountEventKind.DetailsChanged)).Max(e => e.OccurredOn);

    private decimal StartFrom(Money? startingBalance)
    {
        var start = startingBalance ?? Money.Of(AccountSize, Currency);
        if (start.Currency != Currency) throw new CurrencyMismatchException(Currency, start.Currency);
        if (!start.IsPositive) throw new DomainException("The starting balance must be more than zero.");
        return start.Amount;
    }

    private void RequireRestartDate(DateOnly on, DateTime now, string what)
    {
        RequireDate(on, now, what);
        var last = LastLifecycleDate > CurrentRules.EffectiveFrom ? LastLifecycleDate : CurrentRules.EffectiveFrom;
        if (on < last) throw new DomainException($"The {what} date cannot be before the last change ({last:yyyy-MM-dd}).");
    }

    private void AddRules(PropAccountRules rules, DateOnly effectiveFrom, DateTime now)
    {
        var version = _ruleSets.Count == 0 ? 1 : CurrentRules.Version + 1;
        _ruleSets.Add(new PropAccountRuleSet(version, Phase, effectiveFrom, rules.Validated(Currency, StartingBalance), now));
    }

    private void Append(PropAccountEventKind kind, DateOnly on, decimal? startingBalance, string? note, DateTime now)
    {
        _events.Add(new PropAccountEvent(kind, on, Phase, Status, Login, startingBalance, note, now));
        if (kind != PropAccountEventKind.Opened) UpdatedAt = now;
    }

    private void SetDetails(string firm, string nickname, string? login, string? notes)
    {
        Firm = Text.Required(firm, MaxFirmLength, "Firm");
        Nickname = Text.Required(nickname, MaxNicknameLength, "Nickname");
        Login = Text.Optional(login, MaxLoginLength, "Login");
        Notes = Text.Optional(notes, MaxNotesLength, "Notes");
    }

    private void RequireOpen()
    {
        if (Status == PropAccountStatus.Closed) throw new DomainException("A closed account cannot be changed. Reopen it first.");
    }

    private string Format(decimal amount) => Money.Of(amount, Currency).ToString();

    private static void Note(List<string> changes, string what, string? before, string? after)
    {
        if (before == after) return;
        // The notes can be long and say nothing useful twice
        changes.Add(what == "Notes" ? "Notes changed" : $"{what}: {before ?? "none"} to {after ?? "none"}");
    }

    // Tomorrow is allowed because dates are the trader's local dates and "now" is UTC
    private static void RequireDate(DateOnly date, DateTime now, string what)
    {
        if (date == default) throw new DomainException($"The {what} date is required.");
        if (date > DateOnly.FromDateTime(now).AddDays(1)) throw new DomainException($"The {what} date cannot be in the future.");
    }
}

// One entry in an account's history, as the account stood after it. Never edited or deleted.
public class PropAccountEvent
{
    public const int MaxNoteLength = 500;

    public int Id { get; private set; }
    public int PropFirmAccountId { get; private set; }
    public PropAccountEventKind Kind { get; private set; }
    public DateOnly OccurredOn { get; private set; }
    public PropAccountPhase Phase { get; private set; }
    public PropAccountStatus Status { get; private set; }
    public string? Login { get; private set; }

    // Set when this entry restarted or corrected the balance
    public decimal? StartingBalance { get; private set; }
    public string? Note { get; private set; }
    public DateTime RecordedAt { get; private set; }

    private PropAccountEvent() { }

    internal PropAccountEvent(PropAccountEventKind kind, DateOnly occurredOn, PropAccountPhase phase, PropAccountStatus status, string? login, decimal? startingBalance, string? note, DateTime recordedAt)
    {
        Kind = kind;
        OccurredOn = occurredOn;
        Phase = phase;
        Status = status;
        Login = login;
        StartingBalance = startingBalance;
        Note = note;
        RecordedAt = recordedAt;
    }
}

// One version of an account's rules. Versions are only ever added.
public class PropAccountRuleSet
{
    public int Id { get; private set; }
    public int PropFirmAccountId { get; private set; }
    public int Version { get; private set; }
    public PropAccountPhase Phase { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DrawdownModel DrawdownModel { get; private set; }
    public decimal? MaxDrawdown { get; private set; }
    public bool TrailingLocksAtStart { get; private set; }
    public decimal? DailyLossLimit { get; private set; }
    public decimal? ProfitTarget { get; private set; }
    public int? MinimumTradingDays { get; private set; }
    public decimal? PayoutSplitPercent { get; private set; }
    public decimal? PurchaseFee { get; private set; }
    public decimal? ResetFee { get; private set; }
    public decimal? MonthlyFee { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public PropAccountRules Rules => new(DrawdownModel, MaxDrawdown, TrailingLocksAtStart, DailyLossLimit, ProfitTarget, MinimumTradingDays, PayoutSplitPercent, PurchaseFee, ResetFee, MonthlyFee, Notes);

    private PropAccountRuleSet() { }

    internal PropAccountRuleSet(int version, PropAccountPhase phase, DateOnly effectiveFrom, PropAccountRules rules, DateTime now)
    {
        Version = version;
        Phase = phase;
        EffectiveFrom = effectiveFrom;
        DrawdownModel = rules.DrawdownModel;
        MaxDrawdown = rules.MaxDrawdown;
        TrailingLocksAtStart = rules.TrailingLocksAtStart;
        DailyLossLimit = rules.DailyLossLimit;
        ProfitTarget = rules.ProfitTarget;
        MinimumTradingDays = rules.MinimumTradingDays;
        PayoutSplitPercent = rules.PayoutSplitPercent;
        PurchaseFee = rules.PurchaseFee;
        ResetFee = rules.ResetFee;
        MonthlyFee = rules.MonthlyFee;
        Notes = rules.Notes;
        CreatedAt = now;
    }
}
