using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Finance;

namespace MelamuHoldings.API.Controllers;

// Allocation of received payouts: preview (stores nothing), confirm, settle line by line, cancel
[Route("api/allocations")]
public class AllocationsController : FinanceControllerBase
{
    public const string GetRouteName = "GetAllocation";

    private readonly AllocationService _allocations;

    public AllocationsController(AllocationService allocations)
    {
        _allocations = allocations;
    }

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _allocations.ListAsync());

    [HttpGet("{id:int}", Name = GetRouteName)]
    public async Task<IActionResult> Get(int id) => Ok(await _allocations.GetAsync(id));

    [HttpPost("preview")]
    public async Task<IActionResult> Preview(AllocationPreviewRequest request) => Ok(await _allocations.PreviewAsync(request));

    [HttpPost]
    public async Task<IActionResult> Confirm(ConfirmAllocationRequest request, [FromHeader(Name = FinanceHeaders.IdempotencyKey)] string? key)
    {
        if (InvalidIdempotencyKey(key) is { } invalid) return invalid;
        var result = await _allocations.ConfirmAsync(request, key, UserId);
        return result.Replayed ? Replayed(result.Allocation) : CreatedAtRoute(GetRouteName, new { id = result.Allocation.Id }, result.Allocation);
    }

    [HttpPost("{id:int}/lines/{lineId:int}/settle")]
    public async Task<IActionResult> Settle(int id, int lineId, SettleLineRequest request, [FromHeader(Name = FinanceHeaders.IdempotencyKey)] string? key)
    {
        if (InvalidIdempotencyKey(key) is { } invalid) return invalid;
        var result = await _allocations.SettleAsync(id, lineId, request, key, UserId);
        return result.Replayed ? Replayed(result.Allocation) : Ok(result.Allocation);
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancelAllocationRequest request) => Ok(await _allocations.CancelAsync(id, request, UserId));
}

// Versioned split rules. Versions are never edited: a change is a new version.
[Route("api/allocation-policies")]
public class AllocationPoliciesController : FinanceControllerBase
{
    private readonly AllocationService _allocations;

    public AllocationPoliciesController(AllocationService allocations)
    {
        _allocations = allocations;
    }

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _allocations.ListPoliciesAsync());

    [HttpPost]
    public async Task<IActionResult> Create(CreatePolicyRequest request) =>
        StatusCode(StatusCodes.Status201Created, await _allocations.CreatePolicyAsync(request, UserId));

    [HttpPost("{id:int}/activate")]
    public async Task<IActionResult> Activate(int id) => Ok(await _allocations.ActivatePolicyAsync(id, UserId));
}
