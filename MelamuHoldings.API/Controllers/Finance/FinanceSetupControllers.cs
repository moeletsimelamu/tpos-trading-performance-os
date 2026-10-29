using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Finance;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.API.Controllers;

[Route("api/funds")]
public class FundsController : FinanceControllerBase
{
    private readonly FinanceSetupService _setup;

    public FundsController(FinanceSetupService setup)
    {
        _setup = setup;
    }

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _setup.GetFundsAsync());

    [HttpPost]
    public async Task<IActionResult> Create(CreateFundRequest request)
    {
        var fund = await _setup.CreateFundAsync(request, UserId);
        return Created($"/api/funds/{fund.Id}", fund);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateFundRequest request) => Ok(await _setup.UpdateFundAsync(id, request, UserId));
}

[Route("api/financial-accounts")]
public class FinancialAccountsController : FinanceControllerBase
{
    private readonly FinanceSetupService _setup;

    public FinancialAccountsController(FinanceSetupService setup)
    {
        _setup = setup;
    }

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _setup.GetAccountsAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Ok(await _setup.GetAccountAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create(CreateAccountRequest request)
    {
        var account = await _setup.CreateAccountAsync(request, UserId);
        return Created($"/api/financial-accounts/{account.Id}", account);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateAccountRequest request) => Ok(await _setup.UpdateAccountAsync(id, request, UserId));

    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> Close(int id) => Ok(await _setup.CloseAccountAsync(id, UserId));
}

[Route("api")]
public class FinanceOverviewController : FinanceControllerBase
{
    private readonly LedgerService _ledger;
    private readonly IFinanceRepository _repository;

    public FinanceOverviewController(LedgerService ledger, IFinanceRepository repository)
    {
        _ledger = ledger;
        _repository = repository;
    }

    // Balances per account (with fund breakdown), per fund (per currency) and cash totals, optionally as of a date
    [HttpGet("balances")]
    public async Task<IActionResult> Balances(DateOnly? asOf) => Ok(await _ledger.GetBalancesAsync(asOf));

    [HttpGet("currencies")]
    public IActionResult Currencies() => Ok(Currency.Supported.Select(c => new { c.Code, c.MinorUnits }));

    [HttpGet("audit-events")]
    public async Task<IActionResult> AuditEvents(int page = 1, int pageSize = 50)
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            ModelState.AddModelError(nameof(pageSize), "Page must be 1 or more and page size between 1 and 100.");
            return ValidationProblem(ModelState);
        }
        return Ok(await AuditLog.ListAsync(_repository, page, pageSize));
    }
}
