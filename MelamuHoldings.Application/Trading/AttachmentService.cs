using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

// Where attachment files live: outside any public folder, under random names
public interface IAttachmentStore
{
    Task<string> SaveAsync(byte[] content, string extension);
    Stream? Open(string storageKey);
    void Delete(string storageKey);
}

public interface IAttachmentRepository
{
    Task<List<TradeAttachment>> ListAsync(int tradeId);
    Task<TradeAttachment?> GetAsync(int tradeId, int attachmentId);
    void Add(TradeAttachment attachment);
    void Remove(TradeAttachment attachment);
    Task SaveChangesAsync();
}

public record TradeAttachmentDto(int Id, string FileName, string ContentType, long SizeBytes, string? Caption, DateTime UploadedAt);

public record AttachmentFile(Stream Content, string ContentType, string FileName);

// Files kept with a trade. Every call checks that the trade belongs to the caller; another trader's trade is 404.
public class AttachmentService
{
    private readonly ITradeRepository _trades;
    private readonly IAttachmentRepository _attachments;
    private readonly IAttachmentStore _store;
    private readonly TradeHistory _history;
    private readonly TimeProvider _clock;

    public AttachmentService(ITradeRepository trades, IAttachmentRepository attachments, IAttachmentStore store, TradeHistory history,
        TimeProvider clock)
    {
        _history = history;
        _trades = trades;
        _attachments = attachments;
        _store = store;
        _clock = clock;
    }

    public async Task<IReadOnlyList<TradeAttachmentDto>> ListAsync(int tradeId) =>
        (await _attachments.ListAsync(tradeId)).OrderBy(a => a.UploadedAt).ThenBy(a => a.Id).Select(ToDto).ToList();

    public async Task<TradeAttachmentDto> UploadAsync(int tradeId, int userId, Stream content, string fileName, string? caption)
    {
        var trade = await RequireTradeAsync(tradeId, userId);
        if ((await _attachments.ListAsync(tradeId)).Count >= TradeAttachment.MaxPerTrade)
            throw new DomainException($"A trade can keep at most {TradeAttachment.MaxPerTrade} files. Delete one first.");

        // Read at most one byte past the limit, so an oversized file is refused without reading all of it
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > TradeAttachment.MaxBytes)
                throw new DomainException($"The file is larger than {TradeAttachment.MaxBytes / (1024 * 1024)} MB.");
        }
        if (buffer.Length == 0) throw new DomainException("The file is empty.");

        var bytes = buffer.ToArray();
        var type = AttachmentType.Detect(bytes.AsSpan(0, Math.Min(bytes.Length, 16)))
            ?? throw new DomainException("Only PNG, JPEG, WebP images and PDF files can be attached.");

        var key = await _store.SaveAsync(bytes, type.Extension);
        try
        {
            var attachment = new TradeAttachment(tradeId, fileName, type, bytes.Length, key, caption, _clock.GetUtcNow().UtcDateTime);
            _attachments.Add(attachment);
            _history.Record(trade, TradeRevisionKind.FileAdded, [new FieldChange("file", "File", null, attachment.FileName)], attachment.UploadedAt);
            await _attachments.SaveChangesAsync();
            return ToDto(attachment);
        }
        catch
        {
            // The record was not saved, so the file has nothing pointing at it
            _store.Delete(key);
            throw;
        }
    }

    public async Task<AttachmentFile> OpenAsync(int tradeId, int attachmentId, int userId)
    {
        await RequireTradeAsync(tradeId, userId);
        var attachment = await AttachmentAsync(tradeId, attachmentId);
        var stream = _store.Open(attachment.StorageKey) ?? throw new NotFoundException($"The file for attachment {attachmentId} is missing.");
        return new AttachmentFile(stream, attachment.ContentType, attachment.FileName);
    }

    public async Task DeleteAsync(int tradeId, int attachmentId, int userId)
    {
        var trade = await RequireTradeAsync(tradeId, userId);
        var attachment = await AttachmentAsync(tradeId, attachmentId);
        _attachments.Remove(attachment);
        _history.Record(trade, TradeRevisionKind.FileRemoved, [new FieldChange("file", "File", attachment.FileName, null)], _clock.GetUtcNow().UtcDateTime);
        await _attachments.SaveChangesAsync();
        _store.Delete(attachment.StorageKey);
    }

    // After a trade is deleted (its records go with it), its files are removed too
    public void DeleteFiles(IEnumerable<string> storageKeys)
    {
        foreach (var key in storageKeys) _store.Delete(key);
    }

    private async Task<Trade> RequireTradeAsync(int tradeId, int userId) =>
        await _trades.GetForUserAsync(tradeId, userId) ?? throw new NotFoundException($"Trade {tradeId} does not exist.");

    private async Task<TradeAttachment> AttachmentAsync(int tradeId, int attachmentId) =>
        await _attachments.GetAsync(tradeId, attachmentId) ?? throw new NotFoundException($"Attachment {attachmentId} does not exist.");

    private static TradeAttachmentDto ToDto(TradeAttachment a) => new(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.Caption, a.UploadedAt);
}
