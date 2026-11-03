using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Identity;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Infrastructure.Persistence.Configurations;

public class PropFirmAccountConfiguration : IEntityTypeConfiguration<PropFirmAccount>
{
    public void Configure(EntityTypeBuilder<PropFirmAccount> builder)
    {
        builder.Property(a => a.Firm).HasMaxLength(PropFirmAccount.MaxFirmLength).IsRequired();
        builder.Property(a => a.Nickname).HasMaxLength(PropFirmAccount.MaxNicknameLength).IsRequired();
        builder.Property(a => a.Login).HasMaxLength(PropFirmAccount.MaxLoginLength);
        builder.Property(a => a.Notes).HasMaxLength(PropFirmAccount.MaxNotesLength);
        builder.Property(a => a.Currency).IsCurrencyCode();
        builder.Property(a => a.AccountSize).HasColumnType("decimal(19,4)");
        builder.Property(a => a.StartingBalance).HasColumnType("decimal(19,4)");
        builder.Property(a => a.Phase).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.RowVersion).IsRowVersion();
        builder.Ignore(a => a.CurrentRules);

        // Restrict, not cascade: accounts and their history are closed, never deleted
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        // A trader's open accounts have different nicknames; a closed one's can be used again
        builder.HasIndex(a => new { a.UserId, a.Nickname }).IsUnique().HasFilter("[Status] <> 'Closed'").HasDatabaseName(PropAccountService.ActiveNicknameIndex);

        builder.HasMany(a => a.Events).WithOne().HasForeignKey(e => e.PropFirmAccountId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(a => a.Events).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(a => a.RuleSets).WithOne().HasForeignKey(r => r.PropFirmAccountId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(a => a.RuleSets).UsePropertyAccessMode(PropertyAccessMode.Field);
        // Cascade so a reading taken off the collection is deleted with the save
        builder.HasMany(a => a.Balances).WithOne().HasForeignKey(b => b.PropFirmAccountId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Balances).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PropAccountBalanceConfiguration : IEntityTypeConfiguration<PropAccountBalance>
{
    public void Configure(EntityTypeBuilder<PropAccountBalance> builder)
    {
        builder.ToTable("PropAccountBalances");
        builder.Property(b => b.Amount).HasColumnType("decimal(19,4)");
        builder.Property(b => b.DrawdownLevel).HasColumnType("decimal(19,4)");
        builder.Property(b => b.Note).HasMaxLength(PropAccountEvent.MaxNoteLength);
        // One reading per account per day; two requests cannot both add it
        builder.HasIndex(b => new { b.PropFirmAccountId, b.AsOf }).IsUnique().HasDatabaseName(PropAccountService.BalanceDayIndex);
    }
}

public class PropAccountEventConfiguration : IEntityTypeConfiguration<PropAccountEvent>
{
    public void Configure(EntityTypeBuilder<PropAccountEvent> builder)
    {
        builder.ToTable("PropAccountEvents");
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Phase).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Login).HasMaxLength(PropFirmAccount.MaxLoginLength);
        builder.Property(e => e.StartingBalance).HasColumnType("decimal(19,4)");
        builder.Property(e => e.Note).HasMaxLength(PropAccountEvent.MaxNoteLength);
    }
}

public class PropAccountRuleSetConfiguration : IEntityTypeConfiguration<PropAccountRuleSet>
{
    public void Configure(EntityTypeBuilder<PropAccountRuleSet> builder)
    {
        builder.ToTable("PropAccountRuleSets");
        builder.Property(r => r.Phase).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.DrawdownModel).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.MaxDrawdown).HasColumnType("decimal(19,4)");
        builder.Property(r => r.DailyLossLimit).HasColumnType("decimal(19,4)");
        builder.Property(r => r.ProfitTarget).HasColumnType("decimal(19,4)");
        builder.Property(r => r.PayoutSplitPercent).HasColumnType("decimal(5,2)");
        builder.Property(r => r.PurchaseFee).HasColumnType("decimal(19,4)");
        builder.Property(r => r.ResetFee).HasColumnType("decimal(19,4)");
        builder.Property(r => r.MonthlyFee).HasColumnType("decimal(19,4)");
        builder.Property(r => r.Notes).HasMaxLength(PropAccountRules.MaxNotesLength);
        builder.Ignore(r => r.Rules);
        // Versions count up per account; two requests cannot both add the same one
        builder.HasIndex(r => new { r.PropFirmAccountId, r.Version }).IsUnique();
    }
}
