using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Services;

namespace MelamuHoldings.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UploadController : ControllerBase
{
    private readonly UploadService _uploadService;
    private readonly JwtService _jwtService;

    public UploadController(UploadService uploadService, JwtService jwtService)
    {
        _uploadService = uploadService;
        _jwtService = jwtService;
    }

    private int GetUserId() => _jwtService.GetUserIdFromToken(User)!.Value;

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only CSV files are accepted.");

        using var stream = file.OpenReadStream();
        var result = await _uploadService.ProcessUploadAsync(stream, file.FileName, GetUserId());
        return Ok(result);
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetUploadLogs()
    {
        var logs = await _uploadService.GetUploadHistoryAsync(GetUserId());
        return Ok(logs);
    }
}