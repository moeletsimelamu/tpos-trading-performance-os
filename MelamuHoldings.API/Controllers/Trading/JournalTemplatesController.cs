using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Identity;
using MelamuHoldings.Application.Trading;

namespace MelamuHoldings.API.Controllers;

// Reusable, versioned journal templates, owned per trader. Editing the questions adds a version;
// entries already written keep the version they were written with.
[ApiController]
[Route("api/journal-templates")]
[Authorize]
public class JournalTemplatesController : ControllerBase
{
    private readonly JournalTemplateService _templates;
    private readonly JwtService _jwtService;

    public JournalTemplatesController(JournalTemplateService templates, JwtService jwtService)
    {
        _templates = templates;
        _jwtService = jwtService;
    }

    private int UserId => _jwtService.GetUserIdFromToken(User)!.Value;

    [HttpGet]
    public async Task<IActionResult> List(bool includeArchived = false) => Ok(await _templates.ListAsync(UserId, includeArchived));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Ok(await _templates.GetAsync(id, UserId));

    [HttpGet("{id:int}/versions/{version:int}")]
    public async Task<IActionResult> Version(int id, int version) => Ok(await _templates.GetVersionSectionsAsync(id, version, UserId));

    [HttpPost]
    public async Task<IActionResult> Create(SaveTemplateRequest request)
    {
        var created = await _templates.CreateAsync(request, UserId);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveTemplateRequest request) => Ok(await _templates.UpdateAsync(id, request, UserId));

    [HttpPost("{id:int}/duplicate")]
    public async Task<IActionResult> Duplicate(int id, DuplicateTemplateRequest request)
    {
        var copy = await _templates.DuplicateAsync(id, request, UserId);
        return CreatedAtAction(nameof(Get), new { id = copy.Id }, copy);
    }

    [HttpPost("{id:int}/archive")]
    public async Task<IActionResult> Archive(int id) => Ok(await _templates.ArchiveAsync(id, UserId));

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id) => Ok(await _templates.RestoreAsync(id, UserId));

    [HttpPost("{id:int}/make-default")]
    public async Task<IActionResult> MakeDefault(int id) => Ok(await _templates.MakeDefaultAsync(id, UserId));
}
