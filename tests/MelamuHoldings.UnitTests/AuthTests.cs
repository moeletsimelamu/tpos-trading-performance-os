using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using MelamuHoldings.Application.DTOs;
using MelamuHoldings.Application.Services;

namespace MelamuHoldings.UnitTests;

public class AuthTests
{
    private static readonly JwtOptions ValidJwtOptions = new()
    {
        Key = "unit-test-signing-key-that-is-at-least-32-bytes",
        Issuer = "MelamuHoldings",
        Audience = "MelamuHoldings",
        ExpiryHours = 8
    };

    [Theory]
    [InlineData("short", "MelamuHoldings", "MelamuHoldings", 8, false)]
    [InlineData("unit-test-signing-key-that-is-at-least-32-bytes", "", "MelamuHoldings", 8, false)]
    [InlineData("unit-test-signing-key-that-is-at-least-32-bytes", "MelamuHoldings", "MelamuHoldings", 0, false)]
    [InlineData("unit-test-signing-key-that-is-at-least-32-bytes", "MelamuHoldings", "MelamuHoldings", 8, true)]
    public void Jwt_options_require_a_32_byte_key_issuer_audience_and_positive_expiry(
        string key, string issuer, string audience, int expiryHours, bool expected)
    {
        var options = new JwtOptions { Key = key, Issuer = issuer, Audience = audience, ExpiryHours = expiryHours };

        Assert.Equal(expected, options.IsValid());
    }

    [Theory]
    [InlineData("not-an-email", "Owner", "a-long-enough-password", "Email address is not valid.")]
    [InlineData("owner@melamu.test", " ", "a-long-enough-password", "Display name must be between 1 and 100 characters.")]
    [InlineData("owner@melamu.test", "Owner", "elevenchars", "Password must be at least 12 characters.")]
    public async Task Create_owner_rejects_invalid_input(string email, string displayName, string password, string expectedError)
    {
        var users = new InMemoryUserRepository();

        var result = await new OwnerAccountService(users).CreateOwnerAsync(email, displayName, password);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedError, result.Error);
        Assert.Empty(users.Users);
    }

    [Fact]
    public async Task Create_owner_normalises_email_hashes_password_and_rejects_duplicates()
    {
        var users = new InMemoryUserRepository();
        var service = new OwnerAccountService(users);

        var first = await service.CreateOwnerAsync("  Owner@Melamu.Test ", "Owner", "a-long-enough-password");
        var duplicate = await service.CreateOwnerAsync("owner@melamu.test", "Someone", "another-long-password");

        Assert.True(first.Succeeded);
        Assert.False(duplicate.Succeeded);
        var user = Assert.Single(users.Users);
        Assert.Equal("owner@melamu.test", user.Email);
        Assert.NotEqual("a-long-enough-password", user.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("a-long-enough-password", user.PasswordHash));
    }

    [Fact]
    public async Task Login_returns_token_with_configured_expiry_and_rejects_wrong_password()
    {
        var users = new InMemoryUserRepository();
        await new OwnerAccountService(users).CreateOwnerAsync("owner@melamu.test", "Owner", "a-long-enough-password");
        var auth = new AuthService(users, new JwtService(Options.Create(ValidJwtOptions)));

        var success = await auth.LoginAsync(new LoginDto { Email = "owner@melamu.test", Password = "a-long-enough-password" });
        var failure = await auth.LoginAsync(new LoginDto { Email = "owner@melamu.test", Password = "wrong-password-entirely" });

        Assert.NotNull(success);
        Assert.Null(failure);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(success.Token);
        Assert.Equal("MelamuHoldings", token.Issuer);
        Assert.InRange(token.ValidTo, DateTime.UtcNow.AddHours(7.9), DateTime.UtcNow.AddHours(8.1));
        Assert.Equal(token.ValidTo, success.ExpiresAt, TimeSpan.FromSeconds(1));
    }
}
