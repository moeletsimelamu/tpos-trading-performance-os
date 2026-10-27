using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MelamuHoldings.Domain.Identity;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Infrastructure.Persistence.Configurations;

public class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.EntryPrice)
            .HasColumnType("decimal(18,5)");

        builder.Property(t => t.ExitPrice)
            .HasColumnType("decimal(18,5)");

        builder.Property(t => t.RiskAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(t => t.PnL)
            .HasColumnType("decimal(18,2)");

        builder.Property(t => t.RR)
            .HasColumnType("decimal(8,2)");

        builder.Property(t => t.Direction)
            .HasConversion<string>();

        // Journal fields (docs/TRADING_JOURNAL_DESIGN.md §4). Execution values stay empty until recorded.
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.StopLoss).HasColumnType("decimal(18,5)");
        builder.Property(t => t.TakeProfit).HasColumnType("decimal(18,5)");
        builder.Property(t => t.PositionSize).HasColumnType("decimal(18,4)");
        builder.Property(t => t.Fees).HasColumnType("decimal(18,2)");
        builder.Property(t => t.Currency).HasColumnType("char(3)");
        builder.Property(t => t.TradingAccount).HasMaxLength(100);
        builder.Property(t => t.ReviewNotes).HasMaxLength(Trade.MaxReviewNotesLength);
        builder.Property(t => t.IdempotencyKey).HasMaxLength(64);
        builder.Ignore(t => t.IsImported);
        builder.Ignore(t => t.CountsInAnalytics);

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Strategy)
            .WithMany(s => s.Trades)
            .HasForeignKey(t => t.StrategyId)
            .OnDelete(DeleteBehavior.SetNull);

        // Index for fast per-user queries
        builder.HasIndex(t => t.UserId);
        builder.HasIndex(t => new { t.UserId, t.TradeDate });
        builder.HasIndex(t => new { t.UserId, t.Status });
        // A retried save cannot create the same trade twice
        builder.HasIndex(t => new { t.UserId, t.IdempotencyKey }).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
    }
}
