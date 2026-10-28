using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Identity;
using MelamuHoldings.Application.Trading;

namespace MelamuHoldings.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly AnalyticsService _analyticsService;
    private readonly BehaviourAnalyticsService _behaviourService;
    private readonly AdherenceService _adherenceService;
    private readonly JwtService _jwtService;

    public AnalyticsController(
        AnalyticsService analyticsService,
        BehaviourAnalyticsService behaviourService,
        AdherenceService adherenceService,
        JwtService jwtService)
    {
        _analyticsService = analyticsService;
        _behaviourService = behaviourService;
        _adherenceService = adherenceService;
        _jwtService = jwtService;
    }

    private int GetUserId() => _jwtService.GetUserIdFromToken(User)!.Value;

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary() =>
        Ok(await _analyticsService.GetSummaryAsync(GetUserId()));

    [HttpGet("equity-curve")]
    public async Task<IActionResult> GetEquityCurve() =>
        Ok(await _analyticsService.GetEquityCurveAsync(GetUserId()));

    [HttpGet("by-strategy")]
    public async Task<IActionResult> GetByStrategy() =>
        Ok(await _analyticsService.GetStrategyPerformanceAsync(GetUserId()));

    // Rule adherence over closed and reviewed trades, optionally for one strategy
    [HttpGet("adherence")]
    public async Task<IActionResult> GetAdherence(int? strategyId = null) =>
        Ok(await _adherenceService.GetReportAsync(GetUserId(), strategyId));

    [HttpGet("by-day")]
    public async Task<IActionResult> GetByDay() =>
        Ok(await _analyticsService.GetPerformanceByDayAsync(GetUserId()));

    [HttpGet("by-session")]
    public async Task<IActionResult> GetBySession() =>
        Ok(await _analyticsService.GetPerformanceBySessionAsync(GetUserId()));

    [HttpGet("behaviour")]
    public async Task<IActionResult> GetBehaviour() =>
        Ok(await _behaviourService.GetBehaviourSummaryAsync(GetUserId()));
}
