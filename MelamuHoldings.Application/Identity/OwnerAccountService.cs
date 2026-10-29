using System.Net.Mail;
using MelamuHoldings.Domain.Identity;

namespace MelamuHoldings.Application.Identity;

public record CreateOwnerResult(bool Succeeded, string? Error)
{
    public static CreateOwnerResult Ok() => new(true, null);
    public static CreateOwnerResult Fail(string error) => new(false, error);
}

// Accounts are provisioned by the operator through the `create-owner` command; there is no public sign-up.
public class OwnerAccountService
{
    public const int MinimumPasswordLength = 12;

    private readonly IUserRepository _userRepository;

    public OwnerAccountService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<CreateOwnerResult> CreateOwnerAsync(string email, string displayName, string password)
    {
        email = email.Trim().ToLowerInvariant();
        displayName = displayName.Trim();

        if (!MailAddress.TryCreate(email, out _))
            return CreateOwnerResult.Fail("Email address is not valid.");
        if (displayName.Length is 0 or > 100)
            return CreateOwnerResult.Fail("Display name must be between 1 and 100 characters.");
        if (password.Length < MinimumPasswordLength)
            return CreateOwnerResult.Fail($"Password must be at least {MinimumPasswordLength} characters.");
        if (await _userRepository.EmailExistsAsync(email))
            return CreateOwnerResult.Fail("An account with this email already exists.");

        var user = new User
        {
            Email = email,
            DisplayName = displayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();
        return CreateOwnerResult.Ok();
    }
}
