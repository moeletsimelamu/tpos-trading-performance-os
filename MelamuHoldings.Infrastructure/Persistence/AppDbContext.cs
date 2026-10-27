using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Domain.Finance;
using MelamuHoldings.Domain.Identity;
using MelamuHoldings.Domain.Trading;
using MelamuHoldings.Infrastructure.Persistence.Configurations;

namespace MelamuHoldings.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<Strategy> Strategies => Set<Strategy>();
    public DbSet<UploadLog> UploadLogs => Set<UploadLog>();

    public DbSet<Fund> Funds => Set<Fund>();
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Payout> Payouts => Set<Payout>();
    public DbSet<AllocationPolicy> AllocationPolicies => Set<AllocationPolicy>();
    public DbSet<Allocation> Allocations => Set<Allocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new TradeConfiguration());

        modelBuilder.ApplyConfiguration(new FundConfiguration());
        modelBuilder.ApplyConfiguration(new FinancialAccountConfiguration());
        modelBuilder.ApplyConfiguration(new LedgerTransactionConfiguration());
        modelBuilder.ApplyConfiguration(new LedgerEntryConfiguration());
        modelBuilder.ApplyConfiguration(new AuditEventConfiguration());
        modelBuilder.ApplyConfiguration(new PayoutConfiguration());
        modelBuilder.ApplyConfiguration(new AllocationPolicyConfiguration());
        modelBuilder.ApplyConfiguration(new AllocationPolicyLineConfiguration());
        modelBuilder.ApplyConfiguration(new AllocationConfiguration());
        modelBuilder.ApplyConfiguration(new AllocationLineConfiguration());
        modelBuilder.ApplyConfiguration(new AllocationSettlementConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
