using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Identity;
using MelamuHoldings.Application.Trading;

namespace MelamuHoldings.API.Controllers;

// A trader's strategies and their rules. Rules are retired rather than deleted, so checklists keep their meaning.
[ApiController]
[Route("api/strategies")]
[Authorize]
public class StrategiesController : ControllerBase
{
    private readonly StrategyService _strategies;
    private readonly JwtService _jwtService;

    public StrategiesController(StrategyService strategies, JwtService jwtService)
    {
        _strategies = strategies;
        _jwtService = jwtService;
    }

    private int UserId => _jwtService.GetUserIdFromToken(User)!.Value;

    [HttpGet]
    public async Task<IActionResult> List(bool includeArchived = false) => Ok(await _strategies.ListAsync(UserId, includeArchived));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Ok(await _strategies.GetAsync(id, UserId));

    [HttpPost]
    public async Task<IActionResult> Create(SaveStrategyRequest request)
    {
        var created = await _strategies.CreateAsync(request, UserId);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveStrategyRequest request) => Ok(await _strategies.UpdateAsync(id, request, UserId));

    [HttpPost("{id:int}/archive")]
    public async Task<IActionResult> Archive(int id) => Ok(await _strategies.ArchiveAsync(id, UserId));

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id) => Ok(await _strategies.RestoreAsync(id, UserId));

    [HttpPost("{id:int}/rules/{ruleId:int}/restore")]
    public async Task<IActionResult> RestoreRule(int id, int ruleId) => Ok(await _strategies.RestoreRuleAsync(id, ruleId, UserId));
}
