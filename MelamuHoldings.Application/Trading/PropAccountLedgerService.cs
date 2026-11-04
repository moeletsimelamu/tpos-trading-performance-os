using System.ComponentModel.DataAnnotations;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Application.Finance;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public class LinkPropAccountFeeRequest
{
    [Required] public int? TransactionId { get; set; }
    [Required] public PropAccountFeeKind? Kind { get; set; }
}

public record PropAccountCurrencyLineDto(string Currency, decimal Fees, decimal PayoutsReceived, decimal Net);

public record PropAccountEconomicsDto(
    PropAccountOutcome Outcome, IReadOnlyList<PropAccountCurrencyLineDto> Lines, IReadOnlyList<MoneyDto> Awaiting, int FeesCounted, int PayoutsCounted);

public record PropAccountFeeDto(int Id, PropAccountFeeKind Kind, int TransactionId, DateOnly Date, string Description, string? Category, MoneyDto Amount, bool Reversed);

// Amount is what was approved, or what was requested until then; Credited is what actually arrived
public record PropAccountPayoutDto(int Id, string Source, PayoutStatus Status, MoneyDto Amount, DateOnly RequestedOn, DateOnly? ReceivedOn, MoneyDto? Credited);

public record PropAccountLedgerDto(PropAccountEconomicsDto Economics, IReadOnlyList<PropAccountFeeDto> Fees, IReadOnlyList<PropAccountPayoutDto> Payouts);

public record PropAccountReportRowDto(int AccountId, string Firm, string Nickname, PropAccountPhase Phase, PropAccountStatus Status, PropAccountEconomicsDto Economics);

// UnlinkedReceivedPayouts: received payouts not linked to any prop account, so not counted in any row
public record PropAccountReportDto(IReadOnlyList<PropAccountReportRowDto> Accounts, IReadOnlyList<PropAccountCurrencyLineDto> Totals, int UnlinkedReceivedPayouts);

// What each prop firm account has cost and paid back. Fees are expenses already in the ledger and payouts are the payouts
// already recorded: this only says which account they belong to, so nothing here posts to the ledger or changes a balance.
// Owner only, and only the trader's own accounts.
public class PropAccountLedgerService
{
    public const string FeeTransactionIndex = "IX_PropAccountFees_LedgerTransactionId";
    private const int CandidateDays = 60;

    private readonly IFinanceRepository _repository;
    private readonly IPropAccountRepository _propAccounts;
    private readonly TimeProvider _clock;

    public PropAccountLedgerService(IFinanceRepository repository, IPropAccountRepository propAccounts, TimeProvider clock)
    {
        _repository = repository;
        _propAccounts = propAccounts;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    // Every attempt, closed ones included: a closed account that never paid out is the question this answers.
    // Accounts that cost money without paying anything come first.
    public async Task<PropAccountReportDto> ReportAsync(int userId)
    {
        var accounts = await _propAccounts.GetAllForUserAsync(userId);
        var fees = (await FeesAsync(accounts.Select(a => a.Id))).ToLookup(f => f.Row.AccountId);
        var payouts = await _repository.GetPayoutsAsync();
        var linked = payouts.Where(p => p.PropFirmAccountId is not null).ToLookup(p => p.PropFirmAccountId!.Value);

        var rows = accounts
            .Select(a => (Account: a, Economics: Economics(fees[a.Id], linked[a.Id])))
            .OrderBy(r => r.Economics.Outcome).ThenBy(r => r.Account.Status).ThenBy(r => r.Account.Nickname)
            .ToList();
        return new PropAccountReportDto(
            rows.Select(r => new PropAccountReportRowDto(r.Account.Id, r.Account.Firm, r.Account.Nickname, r.Account.Phase, r.Account.Status, ToDto(r.Economics))).ToList(),
            PropAccountEconomics.Totals(rows.Select(r => r.Economics)).Select(ToDto).ToList(),
            payouts.Count(p => p.Status == PayoutStatus.Received && p.PropFirmAccountId is null));
    }

    public async Task<PropAccountLedgerDto> GetAsync(int id, int userId)
    {
        var account = await AccountAsync(id, userId);
        return await LedgerAsync(account.Id);
    }

    // Unlinked, unreversed expenses from shortly before the account opened, newest first, in any currency
    public async Task<IReadOnlyList<PaymentCandidateDto>> FeeCandidatesAsync(int id, int userId)
    {
        var account = await AccountAsync(id, userId);
        var from = account.OpenedOn.AddDays(-CandidateDays);
        var expenses = (await _repository.GetTransactionsAsync(new TransactionQuery(from, null, LedgerTransactionType.Expense, PageSize: 200))).Items;
        var ids = expenses.Select(t => t.Id).ToList();
        var reversed = await _repository.GetReversalIdsAsync(ids);
        var linked = await _repository.GetPropFeeTransactionIdsAsync(ids);
        return expenses
            .Where(t => !reversed.ContainsKey(t.Id) && !linked.Contains(t.Id))
            .Select(t => new PaymentCandidateDto(t.Id, t.TransactionDate, t.Description, t.Category, new MoneyDto(-t.Entries.Sum(e => e.Amount), t.Entries.First().Currency.Code)))
            .ToList();
    }

    public async Task<IReadOnlyList<PropAccountPayoutDto>> PayoutCandidatesAsync(int id, int userId)
    {
        await AccountAsync(id, userId);
        return (await _repository.GetPayoutsAsync())
            .Where(p => p.PropFirmAccountId is null && p.Status != PayoutStatus.Rejected)
            .OrderByDescending(p => p.RequestedOn).ThenByDescending(p => p.Id)
            .Select(ToDto)
            .ToList();
    }

    public async Task<PropAccountLedgerDto> LinkFeeAsync(int id, LinkPropAccountFeeRequest request, int userId)
    {
        var account = await AccountAsync(id, userId);
        var transactionId = request.TransactionId!.Value;
        var expense = await _repository.GetTransactionAsync(transactionId) ?? throw new DomainException($"Transaction {transactionId} does not exist.");
        var reversed = (await _repository.GetReversalIdsAsync([transactionId])).ContainsKey(transactionId);
        var fee = new PropAccountFee(account.Id, request.Kind!.Value, expense, reversed, userId, Now);
        if ((await _repository.GetPropFeeTransactionIdsAsync([transactionId])).Count > 0) throw AlreadyLinked(transactionId);

        try
        {
            await _repository.InTransactionAsync(async () =>
            {
                _repository.Add(fee);
                await _repository.SaveChangesAsync();
                _repository.Add(AuditLog.Event(Now, userId, "propaccount.fee_linked", nameof(PropFirmAccount), account.Id,
                    $"Linked transaction {transactionId} to '{account.Nickname}' as a {fee.Kind.ToString().ToLowerInvariant()} fee.",
                    new { fee.Id, transactionId, fee.Kind }));
                await _repository.SaveChangesAsync();
                return true;
            });
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == FeeTransactionIndex)
        {
            throw AlreadyLinked(transactionId);
        }
        return await LedgerAsync(account.Id);
    }

    public async Task<PropAccountLedgerDto> UnlinkFeeAsync(int id, int feeId, int userId)
    {
        var account = await AccountAsync(id, userId);
        var fee = await _repository.GetPropAccountFeeAsync(feeId);
        if (fee is null || fee.PropFirmAccountId != account.Id) throw new NotFoundException($"Fee {feeId} is not linked to this account.");

        await _repository.InTransactionAsync(async () =>
        {
            _repository.Remove(fee);
            _repository.Add(AuditLog.Event(Now, userId, "propaccount.fee_unlinked", nameof(PropFirmAccount), account.Id,
                $"Removed transaction {fee.LedgerTransactionId} as a fee of '{account.Nickname}'; the expense stays in the ledger.",
                new { fee.Id, transactionId = fee.LedgerTransactionId, fee.Kind }));
            await _repository.SaveChangesAsync();
            return true;
        });
        return await LedgerAsync(account.Id);
    }

    public async Task<PropAccountLedgerDto> LinkPayoutAsync(int id, int payoutId, int userId)
    {
        var account = await AccountAsync(id, userId);
        var payout = await PayoutAsync(payoutId);
        payout.LinkToPropAccount(account.Id);
        _repository.Add(AuditLog.Event(Now, userId, "payout.prop_account_linked", nameof(Payout), payout.Id,
            $"Linked payout #{payout.Id} to prop account '{account.Nickname}'.", new { PropFirmAccountId = account.Id }));
        await _repository.SaveChangesAsync();
        return await LedgerAsync(account.Id);
    }

    public async Task<PropAccountLedgerDto> UnlinkPayoutAsync(int id, int payoutId, int userId)
    {
        var account = await AccountAsync(id, userId);
        var payout = await PayoutAsync(payoutId);
        if (payout.PropFirmAccountId != account.Id) throw new NotFoundException($"Payout {payoutId} is not linked to this account.");

        payout.UnlinkFromPropAccount();
        _repository.Add(AuditLog.Event(Now, userId, "payout.prop_account_unlinked", nameof(Payout), payout.Id,
            $"Removed payout #{payout.Id} from prop account '{account.Nickname}'.", new { PropFirmAccountId = account.Id }));
        await _repository.SaveChangesAsync();
        return await LedgerAsync(account.Id);
    }

    private async Task<PropAccountLedgerDto> LedgerAsync(int accountId)
    {
        var fees = await FeesAsync([accountId]);
        var payouts = (await _repository.GetPayoutsAsync()).Where(p => p.PropFirmAccountId == accountId).ToList();
        return new PropAccountLedgerDto(
            ToDto(Economics(fees, payouts)),
            fees.OrderByDescending(f => f.Row.Date).ThenByDescending(f => f.Row.Id)
                .Select(f => new PropAccountFeeDto(f.Row.Id, f.Row.Kind, f.Row.TransactionId, f.Row.Date, f.Row.Description, f.Row.Category, new MoneyDto(f.Row.Amount, f.Row.Currency), f.Reversed))
                .ToList(),
            payouts.OrderByDescending(p => p.RequestedOn).ThenByDescending(p => p.Id).Select(ToDto).ToList());
    }

    private async Task<List<(PropFeeRow Row, bool Reversed)>> FeesAsync(IEnumerable<int> accountIds)
    {
        var rows = await _repository.GetPropAccountFeesAsync(accountIds);
        var reversed = await _repository.GetReversalIdsAsync(rows.Select(r => r.TransactionId));
        return rows.Select(r => (r, reversed.ContainsKey(r.TransactionId))).ToList();
    }

    private static PropAccountEconomics Economics(IEnumerable<(PropFeeRow Row, bool Reversed)> fees, IEnumerable<Payout> payouts) =>
        PropAccountEconomics.Calculate(fees.Select(f => new PropFeeAmount(Currency.FromCode(f.Row.Currency), f.Row.Amount, f.Reversed)), payouts);

    private async Task<PropFirmAccount> AccountAsync(int id, int userId) =>
        await _propAccounts.GetForUserAsync(id, userId) ?? throw new NotFoundException($"Prop firm account {id} does not exist.");

    private async Task<Payout> PayoutAsync(int id) =>
        await _repository.GetPayoutAsync(id) ?? throw new NotFoundException($"Payout {id} does not exist.");

    private static ConflictException AlreadyLinked(int transactionId) => new($"Transaction {transactionId} is already linked to a prop account.");

    private static PropAccountCurrencyLineDto ToDto(PropAccountCurrencyLine l) => new(l.Currency.Code, l.Fees, l.PayoutsReceived, l.Net);

    private static PropAccountEconomicsDto ToDto(PropAccountEconomics e) =>
        new(e.Outcome, e.Lines.Select(ToDto).ToList(), e.Awaiting.Select(MoneyDto.From).ToList(), e.FeesCounted, e.PayoutsCounted);

    private static PropAccountPayoutDto ToDto(Payout p) => new(
        p.Id, p.Source, p.Status, MoneyDto.From(p.Approved ?? p.Requested), p.RequestedOn, p.ReceivedOn, p.Credited is { } credited ? MoneyDto.From(credited) : null);
}
