using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Finance;

public enum PayoutStatus { Requested, Approved, Received, Rejected }

// A trading payout moving through Requested → Approved → Received (or Rejected).
// A request or an approval is not money: only receiving it posts income to the ledger, and only the amount
// actually credited to an account is available to allocate.
public class Payout
{
    public const int MaxSourceLength = 100;
    public const int MaxReferenceLength = 100;
    public const int MaxNotesLength = 500;
    public const int MaxReasonLength = 200;

    public int Id { get; private set; }

    // Who pays it, e.g. the prop firm. A label for now; Phase 8 links payouts to prop-firm accounts.
    public string Source { get; private set; } = string.Empty;
    public string? Reference { get; private set; }
    public string? Notes { get; private set; }

    // The currency the payout is denominated in, which can differ from the currency it is credited in
    public Currency Currency { get; private set; } = Currency.USD;
    public decimal RequestedAmount { get; private set; }
    public DateOnly RequestedOn { get; private set; }

    public decimal? ApprovedAmount { get; private set; }
    public DateOnly? ApprovedOn { get; private set; }

    public PayoutStatus Status { get; private set; }

    // Set while the payout is received: where the money landed and the income transaction that recorded it.
    // CreditedAmount is in the receiving account's currency and is net of FeeAmount (same currency).
    public DateOnly? ReceivedOn { get; private set; }
    public int? ReceivedIntoAccountId { get; private set; }
    public Currency? CreditedCurrency { get; private set; }
    public decimal? CreditedAmount { get; private set; }
    public decimal? FeeAmount { get; private set; }
    public int? IncomeTransactionId { get; private set; }

    // When credited in another currency: (credited + fee) ÷ approved amount, calculated from the recorded amounts
    public decimal? EffectiveRate { get; private set; }

    public DateOnly? RejectedOn { get; private set; }
    public string? RejectionReason { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public int CreatedByUserId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public byte[] RowVersion { get; private set; } = [];

    public Money Requested => Money.Of(RequestedAmount, Currency);
    public Money? Approved => ApprovedAmount is { } a ? Money.Of(a, Currency) : null;
    public Money? Credited => CreditedAmount is { } c && CreditedCurrency is not null ? Money.Of(c, CreditedCurrency) : null;

    private Payout() { }

    public Payout(string source, string? reference, string? notes, Money requested, DateOnly requestedOn, RecordingInfo recording)
    {
        if (!requested.IsPositive) throw new DomainException("The requested amount must be greater than zero.");
        if (requestedOn == default) throw new DomainException("The request date is required.");
        NotInFuture(requestedOn, recording.RecordedAt, "request");

        Source = Text.Required(source, MaxSourceLength, "Source");
        Reference = Text.Optional(reference, MaxReferenceLength, "Reference");
        Notes = Text.Optional(notes, MaxNotesLength, "Notes");
        Currency = requested.Currency;
        RequestedAmount = requested.Amount;
        RequestedOn = requestedOn;
        Status = PayoutStatus.Requested;
        CreatedAt = recording.RecordedAt;
        CreatedByUserId = recording.UserId;
        IdempotencyKey = recording.IdempotencyKey;
        RequestHash = recording.RequestHash;
    }

    // The approved amount can be lower than requested (a firm may approve less); it can never be higher
    public void Approve(Money? amount, DateOnly approvedOn, DateTime now)
    {
        RequireStatus(PayoutStatus.Requested, "approved");
        var approved = amount ?? Requested;
        if (approved.Currency != Currency) throw new CurrencyMismatchException(Currency, approved.Currency);
        if (!approved.IsPositive) throw new DomainException("The approved amount must be greater than zero.");
        if (approved > Requested) throw new DomainException($"The approved amount cannot exceed the {Requested} requested.");
        RequireOnOrAfter(approvedOn, RequestedOn, "approval", "request");
        NotInFuture(approvedOn, now, "approval");

        ApprovedAmount = approved.Amount;
        ApprovedOn = approvedOn;
        Status = PayoutStatus.Approved;
    }

    public void Reject(string reason, DateOnly rejectedOn, DateTime now)
    {
        if (Status is not (PayoutStatus.Requested or PayoutStatus.Approved))
            throw new DomainException($"A {Status.ToString().ToLowerInvariant()} payout cannot be rejected.");
        RequireOnOrAfter(rejectedOn, ApprovedOn ?? RequestedOn, "rejection", ApprovedOn is null ? "request" : "approval");
        NotInFuture(rejectedOn, now, "rejection");

        RejectionReason = Text.Required(reason, MaxReasonLength, "Reason");
        RejectedOn = rejectedOn;
        Status = PayoutStatus.Rejected;
    }

    // Builds the income transaction for the money that actually arrived; the caller saves it, then calls MarkReceived.
    // `credited` is what reached the account (net); `fee` is anything deducted on the way, in the account's currency.
    public LedgerTransaction BuildReceipt(
        FinancialAccount account, Fund unallocated, Money credited, Money? fee, DateOnly receivedOn,
        string? reference, RecordingInfo recording)
    {
        RequireStatus(PayoutStatus.Approved, "received");
        if (unallocated.Id != Fund.UnallocatedId) throw new DomainException("A received payout is recorded to the Unallocated fund.");
        if (credited.Currency != account.Currency) throw new CurrencyMismatchException(account.Currency, credited.Currency);
        if (!credited.IsPositive) throw new DomainException("The amount credited must be greater than zero.");
        RequireOnOrAfter(receivedOn, ApprovedOn!.Value, "receipt", "approval");

        var gross = fee is null ? credited : credited + fee;
        if (account.Currency == Currency && gross > Approved!)
            throw new DomainException($"{gross} received is more than the {Approved} approved. Check the amount and the fee.");

        var details = new TransactionDetails(receivedOn, $"Payout from {Source}", reference ?? Reference, "Payout");
        return LedgerTransaction.Income(account, unallocated, gross, details, recording, fee);
    }

    public void MarkReceived(LedgerTransaction receipt, FinancialAccount account)
    {
        RequireStatus(PayoutStatus.Approved, "received");
        if (receipt.Type != LedgerTransactionType.Income || receipt.Id == 0)
            throw new DomainException("A payout is received through a saved income transaction.");

        var principal = receipt.Entries.Single(e => e.Role == LedgerEntryRole.Principal).Money;
        var fee = receipt.Entries.SingleOrDefault(e => e.Role == LedgerEntryRole.Fee)?.Money.Negate();

        ReceivedOn = receipt.TransactionDate;
        ReceivedIntoAccountId = account.Id;
        CreditedCurrency = principal.Currency;
        CreditedAmount = (fee is null ? principal : principal - fee).Amount;
        FeeAmount = fee?.Amount;
        IncomeTransactionId = receipt.Id;
        EffectiveRate = principal.Currency == Currency ? null : decimal.Round(principal.Amount / ApprovedAmount!.Value, 8);
        Status = PayoutStatus.Received;
    }

    // Puts a wrongly recorded receipt back to Approved. The caller reverses the income transaction in the ledger,
    // and must first make sure no allocation depends on the money.
    public void UndoReceipt()
    {
        RequireStatus(PayoutStatus.Received, "returned to approved");
        ReceivedOn = null;
        ReceivedIntoAccountId = null;
        CreditedCurrency = null;
        CreditedAmount = null;
        FeeAmount = null;
        IncomeTransactionId = null;
        EffectiveRate = null;
        Status = PayoutStatus.Approved;
    }

    private void RequireStatus(PayoutStatus expected, string action)
    {
        if (Status != expected)
            throw new DomainException($"Only a {expected.ToString().ToLowerInvariant()} payout can be {action}; this one is {Status.ToString().ToLowerInvariant()}.");
    }

    private static void RequireOnOrAfter(DateOnly date, DateOnly earlier, string what, string earlierWhat)
    {
        if (date == default) throw new DomainException($"The {what} date is required.");
        if (date < earlier) throw new DomainException($"The {what} date cannot be before the {earlierWhat} date ({earlier:yyyy-MM-dd}).");
    }

    private static void NotInFuture(DateOnly date, DateTime now, string what)
    {
        if (date > DateOnly.FromDateTime(now).AddDays(1))
            throw new DomainException($"The {what} date cannot be in the future.");
    }
}
