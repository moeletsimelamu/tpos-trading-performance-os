using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var result = await _authService.LoginAsync(dto);
        if (result is null)
            return Problem(title: "Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);

        return Ok(result);
    }
}
