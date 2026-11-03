using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Finance;

namespace MelamuHoldings.API.Controllers;

// Budget versus actual per month and category. A budget never moves money or blocks an expense.
[Route("api/budgets")]
public class BudgetsController : FinanceControllerBase
{
    private readonly BudgetService _budgets;

    public BudgetsController(BudgetService budgets) => _budgets = budgets;

    [HttpGet]
    public async Task<IActionResult> Get(string? month = null) => Ok(await _budgets.GetMonthAsync(month));

    [HttpPost]
    public async Task<IActionResult> Create(CreateBudgetLineRequest request) =>
        StatusCode(StatusCodes.Status201Created, await _budgets.CreateAsync(request, UserId));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateBudgetLineRequest request) => Ok(await _budgets.UpdateAsync(id, request, UserId));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _budgets.DeleteAsync(id, UserId);
        return NoContent();
    }
}
