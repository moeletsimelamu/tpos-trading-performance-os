using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Identity;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.API.Controllers;

// A trader's prop firm accounts, their rules and their history. Nothing here posts to the ledger.
[ApiController]
[Route("api/prop-accounts")]
[Authorize]
public class PropAccountsController : ControllerBase
{
    private readonly PropAccountService _accounts;
    private readonly JwtService _jwtService;

    public PropAccountsController(PropAccountService accounts, JwtService jwtService)
    {
        _accounts = accounts;
        _jwtService = jwtService;
    }

    private int UserId => _jwtService.GetUserIdFromToken(User)!.Value;

    [HttpGet]
    public async Task<IActionResult> List(bool includeClosed = false) => Ok(await _accounts.ListAsync(UserId, includeClosed));

    // The currencies an account can be in (the finance list is for the owner only)
    [HttpGet("currencies")]
    public IActionResult Currencies() => Ok(Currency.Supported.Select(c => c.Code));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Ok(await _accounts.GetAsync(id, UserId));

    [HttpPost]
    public async Task<IActionResult> Create(CreatePropAccountRequest request)
    {
        var created = await _accounts.CreateAsync(request, UserId);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdatePropAccountRequest request) => Ok(await _accounts.UpdateAsync(id, request, UserId));

    [HttpPost("{id:int}/rules")]
    public async Task<IActionResult> ChangeRules(int id, ChangePropAccountRulesRequest request) => Ok(await _accounts.ChangeRulesAsync(id, request, UserId));

    [HttpPost("{id:int}/phase")]
    public async Task<IActionResult> MoveToPhase(int id, MovePropAccountPhaseRequest request) => Ok(await _accounts.MoveToPhaseAsync(id, request, UserId));

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> ChangeStatus(int id, ChangePropAccountStatusRequest request) => Ok(await _accounts.ChangeStatusAsync(id, request, UserId));

    [HttpPost("{id:int}/reset")]
    public async Task<IActionResult> Reset(int id, ResetPropAccountRequest request) => Ok(await _accounts.ResetAsync(id, request, UserId));
}
