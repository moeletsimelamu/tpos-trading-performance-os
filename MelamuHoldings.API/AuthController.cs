using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.DTOs;
using MelamuHoldings.Application.Services;

namespace MelamuHoldings.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.Password) ||
            string.IsNullOrWhiteSpace(dto.DisplayName))
            return BadRequest("All fields are required.");

        if (dto.Password.Length < 8)
            return BadRequest("Password must be at least 8 characters.");

        var result = await _authService.RegisterAsync(dto);
        if (result is null)
            return Conflict("An account with this email already exists.");

        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest("Email and password are required.");

        var result = await _authService.LoginAsync(dto);
        if (result is null)
            return Unauthorized("Invalid email or password.");

        return Ok(result);
    }
}