using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MelamuHoldings.Application.Identity;
using MelamuHoldings.Infrastructure.Persistence;

namespace MelamuHoldings.IntegrationTests;

// Runs the real API against a throwaway SQL Server database created from the EF migrations.
// The server comes from MELAMU_TEST_SQLSERVER (CI uses a SQL Server container); locally it defaults to .\SQLEXPRESS.
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string OwnerEmail = "owner@melamu.test";
    public const string OwnerPassword = "integration-test-password";
    public const string OtherEmail = "other@melamu.test";
    public const string OtherPassword = "another-test-password";

    private readonly string _connectionString;

    public ApiFactory()
    {
        var server = Environment.GetEnvironmentVariable("MELAMU_TEST_SQLSERVER")
            ?? @"Server=.\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True;";
        _connectionString = new SqlConnectionStringBuilder(server)
        {
            InitialCatalog = $"MelamuHoldings_Test_{Guid.NewGuid():N}"
        }.ConnectionString;
    }

    public int LoginAttemptsPerMinute { get; init; } = 100;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" skips appsettings.Development.json and user-secrets, so a missed override can never reach a real database
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = _connectionString,
            ["Jwt:Key"] = "integration-test-signing-key-at-least-32-bytes-long",
            ["Auth:LoginAttemptsPerMinute"] = LoginAttemptsPerMinute.ToString()
        }));
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        var accounts = scope.ServiceProvider.GetRequiredService<OwnerAccountService>();
        await accounts.CreateOwnerAsync(OwnerEmail, "Test Owner", OwnerPassword);
        await accounts.CreateOwnerAsync(OtherEmail, "Other User", OtherPassword);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();

        await base.DisposeAsync();
    }
}
