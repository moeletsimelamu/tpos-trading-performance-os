using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Identity;
using MelamuHoldings.Application.Trading;

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
    public async Task<IActionResult> GetAll() =>
        Ok(await _tradeService.GetTradesByUserAsync(GetUserId()));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var trade = await _tradeService.GetTradeByIdAsync(id, GetUserId());
        return trade is null ? NotFound() : Ok(trade);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTradeDto dto)
    {
        var created = await _tradeService.CreateTradeAsync(dto, GetUserId());
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTradeDto dto)
    {
        var success = await _tradeService.UpdateTradeAsync(id, GetUserId(), dto);
        return success ? NoContent() : NotFound();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var success = await _tradeService.DeleteTradeAsync(id, GetUserId());
        return success ? NoContent() : NotFound();
    }
}
