using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace MelamuHoldings.IntegrationTests;

public class AuthEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AuthEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_with_valid_credentials_returns_token()
    {
        var response = await _factory.CreateClient().LoginAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("token").GetString()));
        Assert.Equal(ApiFactory.OwnerEmail, body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_problem_details_401()
    {
        var response = await _factory.CreateClient().LoginAsync(ApiFactory.OwnerEmail, "not-the-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid email or password.", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Login_with_malformed_email_returns_400()
    {
        var response = await _factory.CreateClient().LoginAsync("not-an-email", "whatever");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Public_registration_endpoint_no_longer_exists()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = "new@melamu.test", password = "a-long-enough-password", displayName = "New" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/trades")]
    [InlineData("/api/analytics/summary")]
    [InlineData("/api/analytics/behaviour")]
    [InlineData("/api/upload/logs")]
    public async Task Protected_endpoints_reject_anonymous_requests(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public class LoginRateLimitTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new() { LoginAttemptsPerMinute = 2 };

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => ((IAsyncLifetime)_factory).DisposeAsync();

    [Fact]
    public async Task Login_is_throttled_after_configured_attempts()
    {
        var client = _factory.CreateClient();

        var first = await client.LoginAsync(ApiFactory.OwnerEmail, "wrong-1");
        var second = await client.LoginAsync(ApiFactory.OwnerEmail, "wrong-2");
        var third = await client.LoginAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }
}

public class StartupConfigurationTests
{
    [Fact]
    public void Api_refuses_to_start_without_a_signing_key()
    {
        // Added after ApiFactory's own configuration, so it wins
        using var factory = new ApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Jwt:Key"] = "" })));

        // ValidateOnStart surfaces the missing key when the host starts, before any request is served
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.IsType<OptionsValidationException>(exception.GetBaseException());
    }
}
