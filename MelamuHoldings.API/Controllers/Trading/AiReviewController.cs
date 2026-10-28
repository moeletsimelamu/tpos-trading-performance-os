using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MelamuHoldings.Application.Identity;
using MelamuHoldings.Application.Trading;

namespace MelamuHoldings.API.Controllers;

// Optional AI-assisted review. Drafts are returned, never saved: the trader edits one and saves it through the
// normal review action. With no AiReview configuration, status says so and drafting returns 503.
[ApiController]
[Authorize]
public class AiReviewController : ControllerBase
{
    private readonly AiReviewService _review;
    private readonly JwtService _jwtService;

    public AiReviewController(AiReviewService review, JwtService jwtService)
    {
        _review = review;
        _jwtService = jwtService;
    }

    private int UserId => _jwtService.GetUserIdFromToken(User)!.Value;

    [HttpGet("api/ai-review/status")]
    public IActionResult Status() => Ok(_review.Status());

    [HttpPost("api/trades/{tradeId:int}/ai-review")]
    [EnableRateLimiting("ai-review")]
    public async Task<IActionResult> Draft(int tradeId, CancellationToken cancellationToken) =>
        Ok(await _review.DraftAsync(tradeId, UserId, cancellationToken));
}
