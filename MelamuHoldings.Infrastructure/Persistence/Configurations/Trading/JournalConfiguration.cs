using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MelamuHoldings.Domain.Identity;
using MelamuHoldings.Domain.Trading;
using MelamuHoldings.Domain.Trading.Journal;

namespace MelamuHoldings.Infrastructure.Persistence.Configurations;

public class JournalTemplateConfiguration : IEntityTypeConfiguration<JournalTemplate>
{
    public void Configure(EntityTypeBuilder<JournalTemplate> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(JournalTemplate.MaxNameLength).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(JournalTemplate.MaxDescriptionLength);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(t => t.Current);

        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        // Exactly one default template per trader
        builder.HasIndex(t => t.UserId).IsUnique().HasFilter("[IsDefault] = 1").HasDatabaseName("IX_JournalTemplates_OneDefault");

        builder.HasMany(t => t.Versions).WithOne().HasForeignKey(v => v.JournalTemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class JournalTemplateVersionConfiguration : IEntityTypeConfiguration<JournalTemplateVersion>
{
    public void Configure(EntityTypeBuilder<JournalTemplateVersion> builder)
    {
        builder.Property(v => v.Schema).IsRequired();
        builder.Ignore(v => v.Sections);
        builder.HasIndex(v => new { v.JournalTemplateId, v.Version }).IsUnique();
    }
}

public class TradeJournalEntryConfiguration : IEntityTypeConfiguration<TradeJournalEntry>
{
    public void Configure(EntityTypeBuilder<TradeJournalEntry> builder)
    {
        builder.Property(e => e.Answers).IsRequired();
        builder.Ignore(e => e.AnswerValues);
        // One set of answers per trade; it goes when the trade is deleted
        builder.HasIndex(e => e.TradeId).IsUnique();
        builder.HasOne<Trade>().WithMany().HasForeignKey(e => e.TradeId).OnDelete(DeleteBehavior.Cascade);
        // A version that has answers is never deleted
        builder.HasOne<JournalTemplateVersion>().WithMany().HasForeignKey(e => e.JournalTemplateVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
