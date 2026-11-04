using System.ComponentModel.DataAnnotations;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

// ---- Responses ----

public record MoneyDto(decimal Amount, string Currency)
{
    public static MoneyDto From(Money money) => new(money.Amount, money.Currency.Code);
}

public record FundPositionDto(int FundId, string FundName, MoneyDto Balance);

public record AccountDto(
    int Id, string Name, FinancialAccountType Type, string Currency, Ownership Ownership,
    string? Institution, string? Notes, FinancialAccountStatus Status,
    MoneyDto Balance, IReadOnlyList<FundPositionDto> Funds, DateTime CreatedAt, DateTime? ClosedAt,
    FinancialAccountCategory Category, TaxTreatment TaxTreatment, string? Reference, DateOnly? OpenedOn, int? HomeFundId, string? HomeFundName);

// An open account whose home is this fund
public record FundAccountDto(int Id, string Name);

// Minimum and Target are in one currency, compared with the fund's balance in that currency only
public record FundDto(int Id, string Name, string? Description, bool IsSystem, IReadOnlyList<MoneyDto> Balances, MoneyDto? Minimum = null, MoneyDto? Target = null, IReadOnlyList<FundAccountDto>? Accounts = null);

public record EntryDto(int AccountId, string AccountName, int FundId, string FundName, decimal Amount, string Currency, LedgerEntryRole Role);

public record TransactionDto(
    int Id, LedgerTransactionType Type, DateOnly Date, DateTime RecordedAt,
    string Description, string? Reference, string? Category, decimal? ExchangeRate, decimal? QuotedRate,
    int? ReversesTransactionId, int? ReversedByTransactionId, IReadOnlyList<EntryDto> Entries);

public record RecordResult(TransactionDto Transaction, bool Replayed);

// Money in bank and cash accounts that is not the business's to count: in personal accounts, or ownership not stated
public record OtherCashDto(Ownership Ownership, MoneyDto Amount);

// Cash totals cover company Bank and Cash accounts only: brokerage balances are not spendable cash, and personal money
// is not the business's. Bank and cash money outside company accounts is in OtherCash, never added to them.
public record BalancesDto(DateOnly? AsOf, IReadOnlyList<AccountDto> Accounts, IReadOnlyList<FundDto> Funds, IReadOnlyList<MoneyDto> CashTotals, IReadOnlyList<OtherCashDto> OtherCash);

public record AuditEventDto(long Id, DateTime OccurredAt, int UserId, string Action, string EntityType, string EntityId, string Summary, string? Data);

// ---- Requests ----
// Nullable value types with [Required] make a missing field a 400 instead of silently becoming 0.

public class CreateFundRequest
{
    [Required, MaxLength(Fund.MaxNameLength)] public string Name { get; set; } = string.Empty;
    [MaxLength(Fund.MaxDescriptionLength)] public string? Description { get; set; }

    // Optional; ThresholdCurrency is required when either is given
    public string? ThresholdCurrency { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Target { get; set; }
}

public class UpdateFundRequest : CreateFundRequest { }

public class CreateAccountRequest : IValidatableObject
{
    [Required, MaxLength(FinancialAccount.MaxNameLength)] public string Name { get; set; } = string.Empty;
    [Required] public FinancialAccountType? Type { get; set; }
    [Required] public string Currency { get; set; } = string.Empty;
    [Required] public Ownership? Ownership { get; set; }
    [MaxLength(FinancialAccount.MaxInstitutionLength)] public string? Institution { get; set; }
    [MaxLength(FinancialAccount.MaxNotesLength)] public string? Notes { get; set; }
    public FinancialAccountCategory Category { get; set; }
    public TaxTreatment TaxTreatment { get; set; }
    [MaxLength(FinancialAccount.MaxReferenceLength)] public string? Reference { get; set; }
    public DateOnly? OpenedOn { get; set; }
    public int? HomeFundId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Domain.Finance.Currency.TryFromCode(Currency, out _))
            yield return new ValidationResult(
                $"Currency must be one of {string.Join(", ", Domain.Finance.Currency.Supported.Select(c => c.Code))}.", [nameof(Currency)]);
    }
}

public class UpdateAccountRequest
{
    [Required, MaxLength(FinancialAccount.MaxNameLength)] public string Name { get; set; } = string.Empty;
    [Required] public Ownership? Ownership { get; set; }
    [MaxLength(FinancialAccount.MaxInstitutionLength)] public string? Institution { get; set; }
    [MaxLength(FinancialAccount.MaxNotesLength)] public string? Notes { get; set; }
    public FinancialAccountCategory Category { get; set; }
    public TaxTreatment TaxTreatment { get; set; }
    [MaxLength(FinancialAccount.MaxReferenceLength)] public string? Reference { get; set; }
    public DateOnly? OpenedOn { get; set; }
    public int? HomeFundId { get; set; }
}

public abstract class TransactionRequest
{
    [Required] public DateOnly? Date { get; set; }
    [Required, MaxLength(LedgerTransaction.MaxDescriptionLength)] public string Description { get; set; } = string.Empty;
    [MaxLength(LedgerTransaction.MaxReferenceLength)] public string? Reference { get; set; }
}

public class OpeningBalanceRequest : TransactionRequest
{
    [Required] public int? AccountId { get; set; }
    [Required] public int? FundId { get; set; }
    [Required] public decimal? Amount { get; set; }
}

public class IncomeRequest : TransactionRequest
{
    [Required] public int? AccountId { get; set; }
    [Required] public int? FundId { get; set; }
    [Required] public decimal? Amount { get; set; }
    [MaxLength(LedgerTransaction.MaxCategoryLength)] public string? Category { get; set; }
}

public class ExpenseRequest : IncomeRequest { }

public class TransferRequest : TransactionRequest
{
    [Required] public int? FromAccountId { get; set; }
    [Required] public int? FromFundId { get; set; }
    [Required] public int? ToAccountId { get; set; }
    [Required] public int? ToFundId { get; set; }
    [Required] public decimal? Amount { get; set; }
}

public class ReallocationRequest : TransactionRequest
{
    [Required] public int? AccountId { get; set; }
    [Required] public int? FromFundId { get; set; }
    [Required] public int? ToFundId { get; set; }
    [Required] public decimal? Amount { get; set; }
}

public class ExchangeRequest : TransactionRequest
{
    [Required] public int? FromAccountId { get; set; }
    [Required] public int? FromFundId { get; set; }
    [Required] public decimal? AmountSent { get; set; }
    [Required] public int? ToAccountId { get; set; }
    [Required] public int? ToFundId { get; set; }
    [Required] public decimal? AmountReceived { get; set; }

    // A conversion fee charged on top, in the currency of the account it was charged to
    [PositiveAmount] public decimal? Fee { get; set; }
    public FeeChargedTo FeeChargedTo { get; set; } = FeeChargedTo.Sending;

    // The rate printed on the provider's confirmation, if there was one; stored for reference only
    public decimal? QuotedRate { get; set; }
}

public enum FeeChargedTo { Sending, Receiving }

public class ReverseRequest
{
    [Required] public DateOnly? Date { get; set; }
    [Required, MaxLength(LedgerTransaction.MaxDescriptionLength)] public string Reason { get; set; } = string.Empty;
}
