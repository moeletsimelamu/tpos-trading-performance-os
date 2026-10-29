using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MelamuHoldings.Domain.Finance;
using MelamuHoldings.Domain.Identity;

namespace MelamuHoldings.Infrastructure.Persistence.Configurations;

public class PayoutConfiguration : IEntityTypeConfiguration<Payout>
{
    public void Configure(EntityTypeBuilder<Payout> builder)
    {
        builder.ToTable("Payouts", FinanceSchema.Name);
        builder.Property(p => p.Source).HasMaxLength(Payout.MaxSourceLength).IsRequired();
        builder.Property(p => p.Reference).HasMaxLength(Payout.MaxReferenceLength);
        builder.Property(p => p.Notes).HasMaxLength(Payout.MaxNotesLength);
        builder.Property(p => p.Currency).IsCurrencyCode();
        builder.Property(p => p.RequestedAmount).HasColumnType("decimal(19,4)");
        builder.Property(p => p.ApprovedAmount).HasColumnType("decimal(19,4)");
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.CreditedCurrency).HasConversion(FinanceSchema.OptionalCurrencyConverter).HasColumnType("char(3)");
        builder.Property(p => p.CreditedAmount).HasColumnType("decimal(19,4)");
        builder.Property(p => p.FeeAmount).HasColumnType("decimal(19,4)");
        builder.Property(p => p.EffectiveRate).HasColumnType("decimal(18,8)");
        builder.Property(p => p.RejectionReason).HasMaxLength(Payout.MaxReasonLength);
        builder.Property(p => p.IdempotencyKey).HasMaxLength(LedgerTransaction.MaxIdempotencyKeyLength).IsRequired();
        builder.Property(p => p.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();
        builder.Ignore(p => p.Requested);
        builder.Ignore(p => p.Approved);
        builder.Ignore(p => p.Credited);

        builder.HasIndex(p => p.IdempotencyKey).IsUnique();
        builder.HasIndex(p => p.IncomeTransactionId).IsUnique().HasFilter("[IncomeTransactionId] IS NOT NULL");
        builder.HasIndex(p => p.Status);

        builder.HasOne<FinancialAccount>().WithMany().HasForeignKey(p => p.ReceivedIntoAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LedgerTransaction>().WithMany().HasForeignKey(p => p.IncomeTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AllocationPolicyConfiguration : IEntityTypeConfiguration<AllocationPolicy>
{
    // The default policy from the brief, seeded as configuration like the funds it refers to (FundConfiguration).
    // It is an ordinary version 1: replace it by creating and activating a new version.
    private static readonly DateTime SeededAt = new(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<AllocationPolicy> builder)
    {
        builder.ToTable("AllocationPolicies", FinanceSchema.Name);
        builder.HasIndex(p => p.Version).IsUnique();
        // At most one active version
        builder.HasIndex(p => p.IsActive).IsUnique().HasFilter("[IsActive] = 1");
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Lines).WithOne().HasForeignKey(l => l.AllocationPolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(p => p.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasData(new { Id = 1, Version = 1, IsActive = true, CreatedAt = SeededAt, CreatedByUserId = (int?)null, ActivatedAt = (DateTime?)SeededAt });
    }
}

public class AllocationPolicyLineConfiguration : IEntityTypeConfiguration<AllocationPolicyLine>
{
    public void Configure(EntityTypeBuilder<AllocationPolicyLine> builder)
    {
        builder.ToTable("AllocationPolicyLines", FinanceSchema.Name);
        builder.Property(l => l.Percentage).HasColumnType("decimal(9,6)");
        builder.HasIndex(l => new { l.AllocationPolicyId, l.FundId }).IsUnique();
        builder.HasOne<Fund>().WithMany().HasForeignKey(l => l.FundId).OnDelete(DeleteBehavior.Restrict);

        // 40 / 30 / 20 / 10 across the four seeded purpose funds (ids 2-5)
        builder.HasData(
            new { Id = 1, AllocationPolicyId = 1, FundId = 2, Percentage = 40m, Position = 1 },
            new { Id = 2, AllocationPolicyId = 1, FundId = 3, Percentage = 30m, Position = 2 },
            new { Id = 3, AllocationPolicyId = 1, FundId = 4, Percentage = 20m, Position = 3 },
            new { Id = 4, AllocationPolicyId = 1, FundId = 5, Percentage = 10m, Position = 4 });
    }
}

public class AllocationConfiguration : IEntityTypeConfiguration<Allocation>
{
    public void Configure(EntityTypeBuilder<Allocation> builder)
    {
        builder.ToTable("Allocations", FinanceSchema.Name);
        builder.Property(a => a.Currency).IsCurrencyCode();
        builder.Property(a => a.Amount).HasColumnType("decimal(19,4)");
        builder.Property(a => a.CancellationReason).HasMaxLength(Allocation.MaxReasonLength);
        builder.Property(a => a.IdempotencyKey).HasMaxLength(LedgerTransaction.MaxIdempotencyKeyLength).IsRequired();
        builder.Property(a => a.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();
        builder.Ignore(a => a.Total);
        builder.Ignore(a => a.IsCancelled);

        builder.HasIndex(a => a.IdempotencyKey).IsUnique();
        // A payout is allocated at most once, unless the earlier allocation was cancelled
        builder.HasIndex(a => a.PayoutId).IsUnique().HasFilter("[CancelledAt] IS NULL");

        builder.HasOne<Payout>().WithMany().HasForeignKey(a => a.PayoutId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AllocationPolicy>().WithMany().HasForeignKey(a => a.AllocationPolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinancialAccount>().WithMany().HasForeignKey(a => a.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.ConfirmedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(a => a.Lines).WithOne().HasForeignKey(l => l.AllocationId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(a => a.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class AllocationLineConfiguration : IEntityTypeConfiguration<AllocationLine>
{
    public void Configure(EntityTypeBuilder<AllocationLine> builder)
    {
        builder.ToTable("AllocationLines", FinanceSchema.Name);
        builder.Property(l => l.FundName).HasMaxLength(Fund.MaxNameLength).IsRequired();
        builder.Property(l => l.Percentage).HasColumnType("decimal(9,6)");
        builder.Property(l => l.Currency).IsCurrencyCode();
        builder.Property(l => l.Amount).HasColumnType("decimal(19,4)");
        builder.Ignore(l => l.Total);
        builder.HasOne<Fund>().WithMany().HasForeignKey(l => l.FundId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(l => l.Settlements).WithOne().HasForeignKey(s => s.AllocationLineId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(l => l.Settlements).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class AllocationSettlementConfiguration : IEntityTypeConfiguration<AllocationSettlement>
{
    public void Configure(EntityTypeBuilder<AllocationSettlement> builder)
    {
        builder.ToTable("AllocationSettlements", FinanceSchema.Name);
        builder.Property(s => s.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Currency).IsCurrencyCode();
        builder.Property(s => s.Amount).HasColumnType("decimal(19,4)");
        builder.Ignore(s => s.Money);

        // One ledger transaction settles one line
        builder.HasIndex(s => s.LedgerTransactionId).IsUnique();
        builder.HasOne<LedgerTransaction>().WithMany().HasForeignKey(s => s.LedgerTransactionId).OnDelete(DeleteBehavior.Restrict);
    }
}
