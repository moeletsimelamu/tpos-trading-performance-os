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
    private readonly AnalyticsScope _scope;
    private readonly JwtService _jwtService;

    public AnalyticsController(
        AnalyticsService analyticsService,
        BehaviourAnalyticsService behaviourService,
        AdherenceService adherenceService,
        AnalyticsScope scope,
        JwtService jwtService)
    {
        _analyticsService = analyticsService;
        _behaviourService = behaviourService;
        _adherenceService = adherenceService;
        _scope = scope;
        _jwtService = jwtService;
    }

    private int GetUserId() => _jwtService.GetUserIdFromToken(User)!.Value;

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] AnalyticsFilter filter) =>
        Ok(await _analyticsService.GetSummaryAsync(GetUserId(), filter));

    [HttpGet("equity-curve")]
    public async Task<IActionResult> GetEquityCurve([FromQuery] AnalyticsFilter filter) =>
        Ok(await _analyticsService.GetEquityCurveAsync(GetUserId(), filter));

    [HttpGet("by-strategy")]
    public async Task<IActionResult> GetByStrategy([FromQuery] AnalyticsFilter filter) =>
        Ok(await _analyticsService.GetStrategyPerformanceAsync(GetUserId(), filter));

    // Rule adherence over closed and reviewed trades, narrowed like every other figure (strategyId included)
    [HttpGet("adherence")]
    public async Task<IActionResult> GetAdherence([FromQuery] AnalyticsFilter filter) =>
        Ok(await _adherenceService.GetReportAsync(GetUserId(), filter));

    // What the filters can be set to: strategies, sessions, accounts and journal questions with fixed answers
    [HttpGet("filter-options")]
    public async Task<IActionResult> GetFilterOptions() => Ok(await _scope.OptionsAsync(GetUserId()));

    [HttpGet("by-day")]
    public async Task<IActionResult> GetByDay([FromQuery] AnalyticsFilter filter) =>
        Ok(await _analyticsService.GetPerformanceByDayAsync(GetUserId(), filter));

    [HttpGet("by-session")]
    public async Task<IActionResult> GetBySession([FromQuery] AnalyticsFilter filter) =>
        Ok(await _analyticsService.GetPerformanceBySessionAsync(GetUserId(), filter));

    [HttpGet("behaviour")]
    public async Task<IActionResult> GetBehaviour([FromQuery] AnalyticsFilter filter) =>
        Ok(await _behaviourService.GetBehaviourSummaryAsync(GetUserId(), filter));
}
