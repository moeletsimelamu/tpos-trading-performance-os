using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MelamuHoldings.Domain.Identity;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Infrastructure.Persistence.Configurations;

public class StrategyConfiguration : IEntityTypeConfiguration<Strategy>
{
    public void Configure(EntityTypeBuilder<Strategy> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(Strategy.MaxNameLength).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(Strategy.MaxDescriptionLength);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(s => s.ActiveRules);

        // Restrict, not cascade: users already cascade to trades, which point at strategies
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        // One strategy per name per trader (case-insensitive, by collation)
        builder.HasIndex(s => new { s.UserId, s.Name }).IsUnique().HasDatabaseName("IX_Strategies_UserId_Name");

        builder.HasMany(s => s.Rules).WithOne().HasForeignKey(r => r.StrategyId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Rules).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(s => s.Trades).UsePropertyAccessMode(PropertyAccessMode.Property);
    }
}

public class StrategyRuleConfiguration : IEntityTypeConfiguration<StrategyRule>
{
    public void Configure(EntityTypeBuilder<StrategyRule> builder)
    {
        builder.Property(r => r.Text).HasMaxLength(StrategyRule.MaxTextLength).IsRequired();
        builder.Ignore(r => r.IsActive);
    }
}

public class TradeChecklistConfiguration : IEntityTypeConfiguration<TradeChecklist>
{
    public void Configure(EntityTypeBuilder<TradeChecklist> builder)
    {
        builder.Ignore(c => c.IsComplete);
        builder.Ignore(c => c.IsCompliant);
        builder.Ignore(c => c.Violations);
        builder.Ignore(c => c.Rules);

        // One checklist per trade and strategy; it goes when the trade is deleted
        builder.HasIndex(c => new { c.TradeId, c.StrategyId }).IsUnique().HasDatabaseName("IX_TradeChecklists_TradeId_StrategyId");
        builder.HasOne<Trade>().WithMany().HasForeignKey(c => c.TradeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Strategy>().WithMany().HasForeignKey(c => c.StrategyId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Items).WithOne().HasForeignKey(i => i.TradeChecklistId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    public void Configure(EntityTypeBuilder<ChecklistItem> builder)
    {
        builder.ToTable("ChecklistItems");
        // The rule's wording and weight as they were when the checklist started
        builder.Property(i => i.Text).HasMaxLength(StrategyRule.MaxTextLength).IsRequired();
        builder.Ignore(i => i.Current);
        builder.HasOne<StrategyRule>().WithMany().HasForeignKey(i => i.StrategyRuleId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Responses).WithOne().HasForeignKey(r => r.ChecklistItemId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Responses).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class TradeAttachmentConfiguration : IEntityTypeConfiguration<TradeAttachment>
{
    public void Configure(EntityTypeBuilder<TradeAttachment> builder)
    {
        builder.Property(a => a.FileName).HasMaxLength(TradeAttachment.MaxFileNameLength).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.StorageKey).HasMaxLength(40).IsRequired();
        builder.Property(a => a.Caption).HasMaxLength(TradeAttachment.MaxCaptionLength);
        builder.Ignore(a => a.IsImage);
        builder.HasIndex(a => a.StorageKey).IsUnique();
        // The record goes with its trade; the service removes the file
        builder.HasOne<Trade>().WithMany().HasForeignKey(a => a.TradeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ImportMatchConfiguration : IEntityTypeConfiguration<ImportMatch>
{
    public void Configure(EntityTypeBuilder<ImportMatch> builder)
    {
        builder.Property(m => m.Row).IsRequired();
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.RowVersion).IsRowVersion();
        builder.Ignore(m => m.Values);
        builder.HasIndex(m => new { m.UserId, m.Status });

        // Deleting the matched trade keeps the row (TradeId becomes null). The upload log and the user are never
        // deleted from under a match; SQL Server also allows only one cascading path into this table.
        builder.HasOne<Trade>().WithMany().HasForeignKey(m => m.TradeId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<UploadLog>().WithMany().HasForeignKey(m => m.UploadLogId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        // A journaled trade is linked to at most one imported row
        builder.HasIndex(m => m.TradeId).IsUnique().HasFilter("[Status] = 'Linked'").HasDatabaseName("IX_ImportMatches_OneLinkPerTrade");
    }
}

public class ChecklistResponseConfiguration : IEntityTypeConfiguration<ChecklistResponse>
{
    public void Configure(EntityTypeBuilder<ChecklistResponse> builder)
    {
        builder.ToTable("ChecklistResponses");
        builder.Property(r => r.Answer).HasConversion<string>().HasMaxLength(20);
        // Answers are appended in sequence; two requests cannot both write the same next answer
        builder.HasIndex(r => new { r.ChecklistItemId, r.Sequence }).IsUnique().HasDatabaseName("IX_ChecklistResponses_ChecklistItemId_Sequence");
    }
}
