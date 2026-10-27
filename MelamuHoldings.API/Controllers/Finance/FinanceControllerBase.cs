using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Finance;
using MelamuHoldings.Application.Identity;

namespace MelamuHoldings.API.Controllers;

public static class FinanceHeaders
{
    // Client-generated key that makes a money-moving POST safe to retry
    public const string IdempotencyKey = "Idempotency-Key";

    // "true" when the response repeats a transaction recorded by an earlier request with the same key
    public const string IdempotentReplayed = "Idempotent-Replayed";
}

[ApiController]
[Authorize(Policy = Policies.Owner)]
public abstract partial class FinanceControllerBase : ControllerBase
{
    protected int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    protected IActionResult? InvalidIdempotencyKey(string? key)
    {
        if (key is null || (key.Length <= 64 && KeyPattern().IsMatch(key))) return null;
        ModelState.AddModelError(FinanceHeaders.IdempotencyKey, "Idempotency-Key must be 1-64 letters, digits, '-' or '_'.");
        return ValidationProblem(ModelState);
    }

    // 201 for a newly recorded transaction; 200 plus Idempotent-Replayed for a retry of one already recorded
    protected IActionResult Recorded(RecordResult result)
    {
        if (!result.Replayed)
            return CreatedAtRoute(TransactionsController.GetRouteName, new { id = result.Transaction.Id }, result.Transaction);

        Response.Headers[FinanceHeaders.IdempotentReplayed] = "true";
        return Ok(result.Transaction);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex KeyPattern();
}
