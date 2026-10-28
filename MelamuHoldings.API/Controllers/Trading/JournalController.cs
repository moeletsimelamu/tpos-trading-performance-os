using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Identity;
using MelamuHoldings.Application.Trading;

namespace MelamuHoldings.API.Controllers;

// The journal at a glance: what needs attention, and a month of trading by day
[ApiController]
[Route("api/journal")]
[Authorize]
public class JournalController : ControllerBase
{
    private readonly JournalOverviewService _overview;
    private readonly JwtService _jwtService;

    public JournalController(JournalOverviewService overview, JwtService jwtService)
    {
        _overview = overview;
        _jwtService = jwtService;
    }

    private int UserId => _jwtService.GetUserIdFromToken(User)!.Value;

    [HttpGet("overview")]
    public async Task<IActionResult> Overview() => Ok(await _overview.GetOverviewAsync(UserId));

    [HttpGet("calendar")]
    public async Task<IActionResult> Calendar(int year, int month) => Ok(await _overview.GetMonthAsync(UserId, year, month));
}
