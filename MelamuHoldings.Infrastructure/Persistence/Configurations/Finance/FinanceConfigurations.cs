using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MelamuHoldings.Domain.Finance;
using MelamuHoldings.Domain.Identity;

namespace MelamuHoldings.Infrastructure.Persistence.Configurations;

// Finance tables live in their own SQL schema; see docs/FINANCIAL_DOMAIN.md "Storage conventions"
internal static class FinanceSchema
{
    public const string Name = "finance";

    public static readonly ValueConverter<Currency, string> CurrencyConverter =
        new(currency => currency.Code, code => Currency.FromCode(code));

    public static PropertyBuilder<Currency> IsCurrencyCode(this PropertyBuilder<Currency> property) =>
        property.HasConversion(CurrencyConverter).HasColumnType("char(3)").IsRequired();
}

public class FundConfiguration : IEntityTypeConfiguration<Fund>
{
    // The funds of the default allocation policy (PLAN.md §3), plus the system fund for money not yet earmarked.
    // These are configuration, not financial records: they hold no money until transactions are posted.
    private static readonly DateTime SeededAt = new(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<Fund> builder)
    {
        builder.ToTable("Funds", FinanceSchema.Name);
        builder.Property(f => f.Name).HasMaxLength(Fund.MaxNameLength).IsRequired();
        builder.HasIndex(f => f.Name).IsUnique();
        builder.Property(f => f.Description).HasMaxLength(Fund.MaxDescriptionLength);

        builder.HasData(
            new { Id = Fund.UnallocatedId, Name = "Unallocated", Description = (string?)"Money received but not yet earmarked for a purpose.", IsSystem = true, CreatedAt = SeededAt },
            new { Id = 2, Name = "Operating Capital", Description = (string?)"Day-to-day running costs of the business.", IsSystem = false, CreatedAt = SeededAt },
            new { Id = 3, Name = "Business Continuity", Description = (string?)"Reserve for setbacks: essential expenses and restoring trading capacity.", IsSystem = false, CreatedAt = SeededAt },
            new { Id = 4, Name = "Wealth Fund", Description = (string?)"Long-term investment contributions.", IsSystem = false, CreatedAt = SeededAt },
            new { Id = 5, Name = "Owner's Distribution", Description = (string?)"Money set aside to pay out to the owner.", IsSystem = false, CreatedAt = SeededAt });
    }
}

public class FinancialAccountConfiguration : IEntityTypeConfiguration<FinancialAccount>
{
    public void Configure(EntityTypeBuilder<FinancialAccount> builder)
    {
        builder.ToTable("FinancialAccounts", FinanceSchema.Name);
        builder.Property(a => a.Name).HasMaxLength(FinancialAccount.MaxNameLength).IsRequired();
        builder.HasIndex(a => a.Name).IsUnique();
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Ownership).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Currency).IsCurrencyCode();
        builder.Property(a => a.Institution).HasMaxLength(FinancialAccount.MaxInstitutionLength);
        builder.Property(a => a.Notes).HasMaxLength(FinancialAccount.MaxNotesLength);
        builder.Ignore(a => a.IsClosed);
    }
}

public class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> builder)
    {
        builder.ToTable("LedgerTransactions", FinanceSchema.Name);
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.Description).HasMaxLength(LedgerTransaction.MaxDescriptionLength).IsRequired();
        builder.Property(t => t.Reference).HasMaxLength(LedgerTransaction.MaxReferenceLength);
        builder.Property(t => t.Category).HasMaxLength(LedgerTransaction.MaxCategoryLength);
        builder.Property(t => t.ExchangeRate).HasColumnType("decimal(18,8)");
        builder.Property(t => t.IdempotencyKey).HasMaxLength(LedgerTransaction.MaxIdempotencyKeyLength).IsRequired();
        builder.Property(t => t.RequestHash).HasMaxLength(64).IsRequired();

        // A retried request can never post twice, and a transaction can be reversed at most once
        builder.HasIndex(t => t.IdempotencyKey).IsUnique();
        builder.HasIndex(t => t.ReversesTransactionId).IsUnique().HasFilter("[ReversesTransactionId] IS NOT NULL");
        builder.HasIndex(t => t.TransactionDate);

        builder.HasOne<LedgerTransaction>().WithMany().HasForeignKey(t => t.ReversesTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Entries).WithOne().HasForeignKey(e => e.LedgerTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(t => t.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("LedgerEntries", FinanceSchema.Name);
        builder.Property(e => e.Amount).HasColumnType("decimal(19,4)");
        builder.Property(e => e.Currency).IsCurrencyCode();
        builder.Ignore(e => e.Money);

        builder.HasOne<FinancialAccount>().WithMany().HasForeignKey(e => e.FinancialAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Fund>().WithMany().HasForeignKey(e => e.FundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.FinancialAccountId, e.FundId });
    }
}

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents", FinanceSchema.Name);
        builder.Property(a => a.Action).HasMaxLength(60).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(60).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(40).IsRequired();
        builder.Property(a => a.Summary).HasMaxLength(300).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.OccurredAt);
    }
}
