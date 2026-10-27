using System.ComponentModel.DataAnnotations;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

// ---- Responses ----

public record PayoutDto(
    int Id, string Source, string? Reference, string? Notes, PayoutStatus Status,
    MoneyDto Requested, DateOnly RequestedOn,
    MoneyDto? Approved, DateOnly? ApprovedOn,
    DateOnly? ReceivedOn, int? ReceivedIntoAccountId, string? ReceivedIntoAccountName,
    MoneyDto? Credited, MoneyDto? Fee, decimal? EffectiveRate, int? IncomeTransactionId,
    DateOnly? RejectedOn, string? RejectionReason, DateTime CreatedAt,
    int? AllocationId, AllocationStatus? AllocationStatus);

public record PayoutResult(PayoutDto Payout, bool Replayed);

public record PolicyLineDto(int FundId, string FundName, decimal Percentage);

public record AllocationPolicyDto(int Id, int Version, bool IsActive, DateTime CreatedAt, DateTime? ActivatedAt, IReadOnlyList<PolicyLineDto> Lines);

public record PreviewLineDto(int FundId, string FundName, decimal Percentage, MoneyDto Amount);

// A preview stores nothing; confirming it sends back the same payout and policy
public record AllocationPreviewDto(int PayoutId, int PolicyId, int PolicyVersion, int SourceAccountId, string SourceAccountName, MoneyDto Total, IReadOnlyList<PreviewLineDto> Lines);

public record SettlementDto(int Id, int TransactionId, SettlementMethod Method, MoneyDto Amount, DateTime RecordedAt, bool Reversed);

public record AllocationLineDto(
    int Id, int FundId, string FundName, decimal Percentage, MoneyDto Amount, MoneyDto Settled, MoneyDto Outstanding,
    IReadOnlyList<SettlementDto> Settlements);

public record AllocationDto(
    int Id, int PayoutId, string PayoutSource, int PolicyId, int PolicyVersion,
    int SourceAccountId, string SourceAccountName, AllocationStatus Status,
    MoneyDto Total, MoneyDto Settled, MoneyDto Outstanding,
    DateTime ConfirmedAt, DateTime? CancelledAt, string? CancellationReason,
    IReadOnlyList<AllocationLineDto> Lines);

public record AllocationResult(AllocationDto Allocation, bool Replayed);

// ---- Requests ----

public class CreatePayoutRequest
{
    [Required, MaxLength(Payout.MaxSourceLength)] public string Source { get; set; } = string.Empty;
    [MaxLength(Payout.MaxReferenceLength)] public string? Reference { get; set; }
    [MaxLength(Payout.MaxNotesLength)] public string? Notes { get; set; }
    [Required] public string Currency { get; set; } = string.Empty;
    [Required] public decimal? Amount { get; set; }
    [Required] public DateOnly? RequestedOn { get; set; }
}

public class ApprovePayoutRequest
{
    // Leave empty to approve the full amount requested
    public decimal? Amount { get; set; }
    [Required] public DateOnly? Date { get; set; }
}

public class RejectPayoutRequest
{
    [Required] public DateOnly? Date { get; set; }
    [Required, MaxLength(Payout.MaxReasonLength)] public string Reason { get; set; } = string.Empty;
}

public class ReceivePayoutRequest
{
    [Required] public int? AccountId { get; set; }

    // What reached the account, in the account's currency
    [Required] public decimal? AmountCredited { get; set; }

    // Anything deducted before the money arrived, in the account's currency
    [PositiveAmount] public decimal? Fee { get; set; }

    [Required] public DateOnly? Date { get; set; }
    [MaxLength(LedgerTransaction.MaxReferenceLength)] public string? Reference { get; set; }
}

public class UndoReceiptRequest
{
    [Required] public DateOnly? Date { get; set; }
    [Required, MaxLength(LedgerTransaction.MaxDescriptionLength)] public string Reason { get; set; } = string.Empty;
}

public class PolicyLineRequest
{
    [Required] public int? FundId { get; set; }
    [Required] public decimal? Percentage { get; set; }
}

public class CreatePolicyRequest
{
    [Required, MinLength(1)] public List<PolicyLineRequest> Lines { get; set; } = new();

    // Make the new version the one used for new allocations straight away
    public bool Activate { get; set; }
}

public class AllocationPreviewRequest
{
    [Required] public int? PayoutId { get; set; }

    // Defaults to the active policy
    public int? PolicyId { get; set; }
}

public class ConfirmAllocationRequest
{
    [Required] public int? PayoutId { get; set; }

    // The policy that was previewed, so the confirmed split is the one the operator saw
    [Required] public int? PolicyId { get; set; }
}

public class CancelAllocationRequest
{
    [Required, MaxLength(Allocation.MaxReasonLength)] public string Reason { get; set; } = string.Empty;
}

public class SettleLineRequest
{
    [Required] public SettlementMethod? Method { get; set; }

    // In the allocation's currency
    [Required] public decimal? Amount { get; set; }

    // Transfer and Exchange: where the money went
    public int? ToAccountId { get; set; }

    // Exchange only: the amount credited after conversion, in the destination account's currency
    public decimal? AmountReceived { get; set; }

    // Exchange only: a conversion fee charged on top, and to which side
    [PositiveAmount] public decimal? Fee { get; set; }
    public FeeChargedTo FeeChargedTo { get; set; } = FeeChargedTo.Sending;
    public decimal? QuotedRate { get; set; }

    [Required] public DateOnly? Date { get; set; }
    [MaxLength(LedgerTransaction.MaxDescriptionLength)] public string? Description { get; set; }
    [MaxLength(LedgerTransaction.MaxReferenceLength)] public string? Reference { get; set; }
}
