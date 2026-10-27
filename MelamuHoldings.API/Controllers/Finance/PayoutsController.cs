using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Finance;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.API.Controllers;

// Payouts from request to receipt. Only /receive touches the ledger.
[Route("api/payouts")]
public class PayoutsController : FinanceControllerBase
{
    public const string GetRouteName = "GetPayout";

    private readonly PayoutService _payouts;

    public PayoutsController(PayoutService payouts)
    {
        _payouts = payouts;
    }

    [HttpGet]
    public async Task<IActionResult> List(PayoutStatus? status) => Ok(await _payouts.ListAsync(status));

    [HttpGet("{id:int}", Name = GetRouteName)]
    public async Task<IActionResult> Get(int id) => Ok(await _payouts.GetAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create(CreatePayoutRequest request, [FromHeader(Name = FinanceHeaders.IdempotencyKey)] string? key) =>
        InvalidIdempotencyKey(key) ?? Created(await _payouts.CreateAsync(request, key, UserId));

    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, ApprovePayoutRequest request) =>
        Ok(await _payouts.ApproveAsync(id, request, UserId));

    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, RejectPayoutRequest request) =>
        Ok(await _payouts.RejectAsync(id, request, UserId));

    [HttpPost("{id:int}/receive")]
    public async Task<IActionResult> Receive(int id, ReceivePayoutRequest request, [FromHeader(Name = FinanceHeaders.IdempotencyKey)] string? key) =>
        InvalidIdempotencyKey(key) ?? Updated(await _payouts.ReceiveAsync(id, request, key, UserId));

    [HttpPost("{id:int}/undo-receipt")]
    public async Task<IActionResult> UndoReceipt(int id, UndoReceiptRequest request, [FromHeader(Name = FinanceHeaders.IdempotencyKey)] string? key) =>
        InvalidIdempotencyKey(key) ?? Updated(await _payouts.UndoReceiptAsync(id, request, key, UserId));

    private IActionResult Created(PayoutResult result) =>
        result.Replayed ? Replayed(result.Payout) : CreatedAtRoute(GetRouteName, new { id = result.Payout.Id }, result.Payout);

    private IActionResult Updated(PayoutResult result) => result.Replayed ? Replayed(result.Payout) : Ok(result.Payout);
}
