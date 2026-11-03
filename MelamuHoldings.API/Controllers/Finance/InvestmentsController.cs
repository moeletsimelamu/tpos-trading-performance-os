using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Finance;

namespace MelamuHoldings.API.Controllers;

// Brokerage accounts, their recorded valuations, and the unrealised gain or loss against the ledger.
// A valuation never moves money.
[Route("api/investments")]
public class InvestmentsController : FinanceControllerBase
{
    private readonly InvestmentService _investments;

    public InvestmentsController(InvestmentService investments) => _investments = investments;

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await _investments.GetAsync());

    [HttpGet("{accountId:int}/valuations")]
    public async Task<IActionResult> Valuations(int accountId) => Ok(await _investments.ListValuationsAsync(accountId));

    [HttpPost("{accountId:int}/valuations")]
    public async Task<IActionResult> Record(int accountId, RecordValuationRequest request) =>
        StatusCode(StatusCodes.Status201Created, await _investments.RecordAsync(accountId, request, UserId));

    [HttpDelete("{accountId:int}/valuations/{valuationId:int}")]
    public async Task<IActionResult> Delete(int accountId, int valuationId)
    {
        await _investments.DeleteAsync(accountId, valuationId, UserId);
        return NoContent();
    }
}
