using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Diagnostics;
using MelamuHoldings.Domain.Entities;

namespace MelamuHoldings.Infrastructure.Data.Configurations;

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

        builder.HasOne(t => t.User)
            .WithMany(u => u.Trades)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Strategy)
            .WithMany(s => s.Trades)
            .HasForeignKey(t => t.StrategyId)
            .OnDelete(DeleteBehavior.SetNull);

        // Index for fast per-user queries
        builder.HasIndex(t => t.UserId);
        builder.HasIndex(t => new { t.UserId, t.TradeDate });
    }
}