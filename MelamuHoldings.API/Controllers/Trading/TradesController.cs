using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Identity;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TradesController : ControllerBase
{
    private readonly TradeService _tradeService;
    private readonly JwtService _jwtService;

    public TradesController(TradeService tradeService, JwtService jwtService)
    {
        _tradeService = tradeService;
        _jwtService = jwtService;
    }

    private int GetUserId() => _jwtService.GetUserIdFromToken(User)!.Value;

    [HttpGet]
    public async Task<IActionResult> GetAll(TradeStatus? status) =>
        Ok(await _tradeService.GetTradesByUserAsync(GetUserId(), status));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var trade = await _tradeService.GetTradeByIdAsync(id, GetUserId());
        return trade is null ? NotFound() : Ok(trade);
    }

    // 201 for a new trade; 200 plus Idempotent-Replayed when the same Idempotency-Key already created one
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTradeDto dto, [FromHeader(Name = FinanceHeaders.IdempotencyKey)] string? key)
    {
        if (key is { Length: > 64 })
        {
            ModelState.AddModelError(FinanceHeaders.IdempotencyKey, "Idempotency-Key must be at most 64 characters.");
            return ValidationProblem(ModelState);
        }

        var result = await _tradeService.CreateTradeAsync(dto, GetUserId(), key);
        if (!result.Replayed) return CreatedAtAction(nameof(GetById), new { id = result.Trade.Id }, result.Trade);
        Response.Headers[FinanceHeaders.IdempotentReplayed] = "true";
        return Ok(result.Trade);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTradeDto dto)
    {
        var updated = await _tradeService.UpdateTradeAsync(id, GetUserId(), dto);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("{id}/review")]
    public async Task<IActionResult> Review(int id, [FromBody] ReviewTradeDto dto)
    {
        var reviewed = await _tradeService.ReviewTradeAsync(id, GetUserId(), dto);
        return reviewed is null ? NotFound() : Ok(reviewed);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var success = await _tradeService.DeleteTradeAsync(id, GetUserId());
        return success ? NoContent() : NotFound();
    }
}
