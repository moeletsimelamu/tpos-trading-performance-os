using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Finance;

namespace MelamuHoldings.API.Controllers;

// How much cash is free to use, per currency, with the working behind it
[Route("api/unrestricted-cash")]
public class UnrestrictedCashController : FinanceControllerBase
{
    private readonly UnrestrictedCashService _unrestricted;

    public UnrestrictedCashController(UnrestrictedCashService unrestricted) => _unrestricted = unrestricted;

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await _unrestricted.GetAsync());
}
