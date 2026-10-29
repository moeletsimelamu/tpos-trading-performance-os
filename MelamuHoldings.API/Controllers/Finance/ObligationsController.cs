using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Finance;

namespace MelamuHoldings.API.Controllers;

// What the business has to pay and when. Payments link expenses already in the ledger; nothing here moves money.
[Route("api/obligations")]
public class ObligationsController : FinanceControllerBase
{
    private readonly ObligationService _obligations;

    public ObligationsController(ObligationService obligations) => _obligations = obligations;

    [HttpGet]
    public async Task<IActionResult> List(bool includeClosed = false) => Ok(await _obligations.ListAsync(includeClosed));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Ok(await _obligations.GetAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create(SaveObligationRequest request)
    {
        var created = await _obligations.CreateAsync(request, UserId);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveObligationRequest request) => Ok(await _obligations.UpdateAsync(id, request, UserId));

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id) => Ok(await _obligations.CancelAsync(id, UserId));

    [HttpGet("{id:int}/payment-candidates")]
    public async Task<IActionResult> Candidates(int id) => Ok(await _obligations.CandidatesAsync(id));

    [HttpPost("{id:int}/payments")]
    public async Task<IActionResult> RecordPayment(int id, RecordObligationPaymentRequest request) =>
        Ok(await _obligations.RecordPaymentAsync(id, request, UserId));

    // Removes the most recent payment link; the expense stays in the ledger
    [HttpPost("{id:int}/payments/undo")]
    public async Task<IActionResult> UndoPayment(int id) => Ok(await _obligations.UndoLastPaymentAsync(id, UserId));
}
