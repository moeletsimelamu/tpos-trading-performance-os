using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Finance;

namespace MelamuHoldings.API.Controllers;

// How close continuity is to its target, and the settings it is measured by
[Route("api/continuity")]
public class ContinuityController : FinanceControllerBase
{
    private readonly ContinuityService _continuity;

    public ContinuityController(ContinuityService continuity) => _continuity = continuity;

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await _continuity.GetReportAsync());

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(UpdateContinuitySettingsRequest request) =>
        Ok(await _continuity.UpdateSettingsAsync(request, UserId));
}
