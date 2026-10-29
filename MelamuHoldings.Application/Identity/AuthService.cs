namespace MelamuHoldings.Application.Identity;

public class AuthService
{
    private readonly IUserRepository _userRepository;
    private readonly JwtService _jwtService;

    public AuthService(IUserRepository userRepository, JwtService jwtService)
    {
        _userRepository = userRepository;
        _jwtService = jwtService;
    }

    public async Task<AuthResponseDto?> LoginAsync(LoginDto dto)
    {
        var user = await _userRepository.GetByEmailAsync(dto.Email);
        if (user is null) return null;

        var passwordValid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
        if (!passwordValid) return null;

        var (token, expiresAt) = _jwtService.GenerateToken(user);
        return new AuthResponseDto
        {
            Token = token,
            Email = user.Email,
            DisplayName = user.DisplayName,
            ExpiresAt = expiresAt
        };
    }
}
