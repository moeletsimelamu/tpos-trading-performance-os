using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Trading;

namespace MelamuHoldings.API.Controllers;

// Which fees and payouts belong to which prop firm account. Links only, so owner only and nothing here posts to the ledger.
[Route("api/prop-accounts")]
public class PropAccountLedgerController : FinanceControllerBase
{
    private readonly PropAccountLedgerService _ledger;

    public PropAccountLedgerController(PropAccountLedgerService ledger)
    {
        _ledger = ledger;
    }

    [HttpGet("fees-and-payouts")]
    public async Task<IActionResult> Report() => Ok(await _ledger.ReportAsync(UserId));

    [HttpGet("{id:int}/ledger")]
    public async Task<IActionResult> Get(int id) => Ok(await _ledger.GetAsync(id, UserId));

    [HttpGet("{id:int}/ledger/fee-candidates")]
    public async Task<IActionResult> FeeCandidates(int id) => Ok(await _ledger.FeeCandidatesAsync(id, UserId));

    [HttpPost("{id:int}/ledger/fees")]
    public async Task<IActionResult> LinkFee(int id, LinkPropAccountFeeRequest request) => Ok(await _ledger.LinkFeeAsync(id, request, UserId));

    // Removes the link; the expense stays in the ledger
    [HttpDelete("{id:int}/ledger/fees/{feeId:int}")]
    public async Task<IActionResult> UnlinkFee(int id, int feeId) => Ok(await _ledger.UnlinkFeeAsync(id, feeId, UserId));

    [HttpGet("{id:int}/ledger/payout-candidates")]
    public async Task<IActionResult> PayoutCandidates(int id) => Ok(await _ledger.PayoutCandidatesAsync(id, UserId));

    [HttpPost("{id:int}/ledger/payouts/{payoutId:int}")]
    public async Task<IActionResult> LinkPayout(int id, int payoutId) => Ok(await _ledger.LinkPayoutAsync(id, payoutId, UserId));

    [HttpDelete("{id:int}/ledger/payouts/{payoutId:int}")]
    public async Task<IActionResult> UnlinkPayout(int id, int payoutId) => Ok(await _ledger.UnlinkPayoutAsync(id, payoutId, UserId));
}
