using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelamuHoldings.Application.Identity;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.API.Controllers;

// Files kept with a trade, served only to the trade's owner. Images are shown inline; PDFs are downloaded.
[ApiController]
[Route("api/trades/{tradeId:int}/attachments")]
[Authorize]
public class TradeAttachmentsController : ControllerBase
{
    private readonly AttachmentService _attachments;
    private readonly JwtService _jwtService;

    public TradeAttachmentsController(AttachmentService attachments, JwtService jwtService)
    {
        _attachments = attachments;
        _jwtService = jwtService;
    }

    private int UserId => _jwtService.GetUserIdFromToken(User)!.Value;

    [HttpPost]
    [RequestSizeLimit(TradeAttachment.MaxBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = TradeAttachment.MaxBytes + 64 * 1024)]
    public async Task<IActionResult> Upload(int tradeId, IFormFile? file, [FromForm] string? caption)
    {
        if (file is null || file.Length == 0)
            return Problem(title: "Choose a file to attach.", statusCode: StatusCodes.Status400BadRequest);

        await using var stream = file.OpenReadStream();
        var created = await _attachments.UploadAsync(tradeId, UserId, stream, file.FileName, caption);
        return CreatedAtAction(nameof(Download), new { tradeId, id = created.Id }, created);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Download(int tradeId, int id)
    {
        var file = await _attachments.OpenAsync(tradeId, id, UserId);
        var inline = file.ContentType.StartsWith("image/", StringComparison.Ordinal);
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment") { FileNameStar = file.FileName }.ToString();
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, no-store";
        return File(file.Content, file.ContentType);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int tradeId, int id)
    {
        await _attachments.DeleteAsync(tradeId, id, UserId);
        return NoContent();
    }
}
