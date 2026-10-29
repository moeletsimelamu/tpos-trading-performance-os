using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Finance;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.API.Controllers;

// One endpoint per kind of money movement. There is deliberately no endpoint that edits or deletes
// a transaction or sets a balance: corrections are reversals.
[Route("api/transactions")]
public class TransactionsController : FinanceControllerBase
{
    public const string GetRouteName = "GetTransaction";

    private readonly LedgerService _ledger;

    public TransactionsController(LedgerService ledger)
    {
        _ledger = ledger;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        DateOnly? from, DateOnly? to, LedgerTransactionType? type, int? accountId, int? fundId,
        int page = 1, int pageSize = 25, bool cash = false)
    {
        if (page < 1) ModelState.AddModelError(nameof(page), "Page must be 1 or more.");
        if (pageSize is < 1 or > LedgerService.MaxPageSize) ModelState.AddModelError(nameof(pageSize), $"Page size must be between 1 and {LedgerService.MaxPageSize}.");
        if (from > to) ModelState.AddModelError(nameof(from), "'from' must not be after 'to'.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        return Ok(await _ledger.ListAsync(new TransactionQuery(from, to, type, accountId, fundId, page, pageSize, cash)));
    }

    [HttpGet("{id:int}", Name = GetRouteName)]
    public async Task<IActionResult> Get(int id) => Ok(await _ledger.GetAsync(id));

    [HttpPost("opening-balance")]
    public async Task<IActionResult> OpeningBalance(OpeningBalanceRequest request, [FromHeader(Name = FinanceHeaders.IdempotencyKey)] string? key) =>
        InvalidIdempotencyKey(key) ?? Recorded(await _ledger.RecordOpeningBalanceAsync(request, key, UserId));

    [HttpPost("income")]
    public async Task<IActionResult> Income(IncomeRequest request, [FromHeader(Name = FinanceHeaders.IdempotencyKey)] string? key) =>
        InvalidIdempotencyKey(key) ?? Recorded(await _ledger.RecordIncomeAsync(request, key, UserId));

    [HttpPost("expense")]
    public async Task<IActionResult> Expense(ExpenseRequest request, [FromHeader(Name = FinanceHeaders.IdempotencyKey)] string? key) =>
        InvalidIdempotencyKey(key) ?? Recorded(await _ledger.RecordExpenseAsync(request, key, UserId));

    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer(TransferRequest request, [FromHeader(Name = FinanceHeaders.IdempotencyKey)] string? key) =>
        InvalidIdempotencyKey(key) ?? Recorded(await _ledger.RecordTransferAsync(request, key, UserId));

    [HttpPost("reallocation")]
    public async Task<IActionResult> Reallocation(ReallocationRequest request, [FromHeader(Name = FinanceHeaders.IdempotencyKey)] string? key) =>
        InvalidIdempotencyKey(key) ?? Recorded(await _ledger.RecordReallocationAsync(request, key, UserId));

    [HttpPost("exchange")]
    public async Task<IActionResult> Exchange(ExchangeRequest request, [FromHeader(Name = FinanceHeaders.IdempotencyKey)] string? key) =>
        InvalidIdempotencyKey(key) ?? Recorded(await _ledger.RecordExchangeAsync(request, key, UserId));

    [HttpPost("{id:int}/reverse")]
    public async Task<IActionResult> Reverse(int id, ReverseRequest request, [FromHeader(Name = FinanceHeaders.IdempotencyKey)] string? key) =>
        InvalidIdempotencyKey(key) ?? Recorded(await _ledger.ReverseAsync(id, request, key, UserId));
}
