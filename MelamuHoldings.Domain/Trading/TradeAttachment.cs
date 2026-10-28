using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Trading;

// A file kept with a trade: a chart screenshot or a platform statement. The type is decided from the file's
// first bytes, never from its name or the browser's claim, and only a few safe types are accepted.
public class TradeAttachment
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxPerTrade = 10;
    public const int MaxFileNameLength = 200;
    public const int MaxCaptionLength = 200;

    public int Id { get; private set; }
    public int TradeId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }

    // Where the file is kept: a random name, never the uploaded one
    public string StorageKey { get; private set; } = string.Empty;
    public string? Caption { get; private set; }
    public DateTime UploadedAt { get; private set; }

    public bool IsImage => ContentType.StartsWith("image/", StringComparison.Ordinal);

    private TradeAttachment() { }

    public TradeAttachment(int tradeId, string fileName, AttachmentType type, long sizeBytes, string storageKey, string? caption, DateTime now)
    {
        if (sizeBytes is <= 0 or > MaxBytes) throw new DomainException($"A file must be between 1 byte and {MaxBytes / (1024 * 1024)} MB.");
        TradeId = tradeId;
        FileName = SafeName(fileName, type);
        ContentType = type.ContentType;
        SizeBytes = sizeBytes;
        StorageKey = storageKey;
        Caption = Text.Optional(caption, MaxCaptionLength, "Caption");
        UploadedAt = now;
    }

    // The name shown and offered on download: no folders, no control or reserved characters, the right extension
    private static string SafeName(string fileName, AttachmentType type)
    {
        var name = Path.GetFileNameWithoutExtension(fileName.Replace('\\', '/').Split('/').Last());
        name = new string(name.Where(c => !char.IsControl(c) && !"<>:\"/\\|?*".Contains(c)).ToArray()).Trim().Trim('.');
        if (name.Length == 0) name = "attachment";
        var max = MaxFileNameLength - type.Extension.Length;
        return (name.Length > max ? name[..max] : name) + type.Extension;
    }
}

public sealed record AttachmentType(string ContentType, string Extension)
{
    public static readonly AttachmentType Png = new("image/png", ".png");
    public static readonly AttachmentType Jpeg = new("image/jpeg", ".jpg");
    public static readonly AttachmentType WebP = new("image/webp", ".webp");
    public static readonly AttachmentType Pdf = new("application/pdf", ".pdf");

    public static IReadOnlyList<AttachmentType> Accepted { get; } = [Png, Jpeg, WebP, Pdf];

    // Recognises the accepted types by their signature; anything else (including SVG and HTML) is refused
    public static AttachmentType? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return Png;
        if (header.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return Jpeg;
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return WebP;
        if (header.StartsWith("%PDF-"u8)) return Pdf;
        return null;
    }
}
