using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Finance;

namespace MelamuHoldings.API.Controllers;

// What the business intends to buy, and whether it can be afforded. A purchase links an expense already in the ledger.
[Route("api/planned-purchases")]
public class PlannedPurchasesController : FinanceControllerBase
{
    private readonly PlannedPurchaseService _purchases;

    public PlannedPurchasesController(PlannedPurchaseService purchases) => _purchases = purchases;

    [HttpGet]
    public async Task<IActionResult> List(bool includeClosed = false) => Ok(await _purchases.ListAsync(includeClosed));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Ok(await _purchases.GetAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create(SavePlannedPurchaseRequest request)
    {
        var created = await _purchases.CreateAsync(request, UserId);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SavePlannedPurchaseRequest request) => Ok(await _purchases.UpdateAsync(id, request, UserId));

    [HttpPost("{id:int}/commit")]
    public async Task<IActionResult> Commit(int id) => Ok(await _purchases.CommitAsync(id, UserId));

    [HttpPost("{id:int}/withdraw-commitment")]
    public async Task<IActionResult> WithdrawCommitment(int id) => Ok(await _purchases.WithdrawCommitmentAsync(id, UserId));

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id) => Ok(await _purchases.CancelAsync(id, UserId));

    [HttpGet("{id:int}/expense-candidates")]
    public async Task<IActionResult> Candidates(int id) => Ok(await _purchases.CandidatesAsync(id));

    [HttpPost("{id:int}/purchase")]
    public async Task<IActionResult> RecordPurchase(int id, RecordPurchaseRequest request) => Ok(await _purchases.RecordPurchaseAsync(id, request, UserId));

    [HttpPost("{id:int}/purchase/undo")]
    public async Task<IActionResult> UndoPurchase(int id) => Ok(await _purchases.UndoPurchaseAsync(id, UserId));
}
