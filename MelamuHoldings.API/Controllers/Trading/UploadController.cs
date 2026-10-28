using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Identity;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UploadController : ControllerBase
{
    private readonly UploadService _uploadService;
    private readonly ImportReconciliationService _reconciliation;
    private readonly JwtService _jwtService;

    public UploadController(UploadService uploadService, ImportReconciliationService reconciliation, JwtService jwtService)
    {
        _uploadService = uploadService;
        _reconciliation = reconciliation;
        _jwtService = jwtService;
    }

    private int GetUserId() => _jwtService.GetUserIdFromToken(User)!.Value;

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return Problem(title: "No file provided.", statusCode: StatusCodes.Status400BadRequest);

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return Problem(title: "Only CSV files are accepted.", statusCode: StatusCodes.Status400BadRequest);

        using var stream = file.OpenReadStream();
        var result = await _uploadService.ProcessUploadAsync(stream, file.FileName, GetUserId());
        return Ok(result);
    }

    // CSV rows that matched trades journaled by hand, by default those still waiting for a decision
    [HttpGet("matches")]
    public async Task<IActionResult> GetMatches(ImportMatchStatus? status = ImportMatchStatus.Pending) =>
        Ok(await _reconciliation.ListAsync(GetUserId(), status));

    [HttpPost("matches/{id:int}/link")]
    public async Task<IActionResult> Link(int id) => Ok(await _reconciliation.LinkAsync(id, GetUserId()));

    [HttpPost("matches/{id:int}/import")]
    public async Task<IActionResult> ImportSeparately(int id) => Ok(await _reconciliation.ImportSeparatelyAsync(id, GetUserId()));

    [HttpPost("matches/{id:int}/dismiss")]
    public async Task<IActionResult> Dismiss(int id) => Ok(await _reconciliation.DismissAsync(id, GetUserId()));

    [HttpGet("logs")]
    public async Task<IActionResult> GetUploadLogs()
    {
        var logs = await _uploadService.GetUploadHistoryAsync(GetUserId());
        return Ok(logs);
    }
}
