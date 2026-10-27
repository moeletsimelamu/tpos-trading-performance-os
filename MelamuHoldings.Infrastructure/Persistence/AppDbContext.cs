using Microsoft.EntityFrameworkCore;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new TradeConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
