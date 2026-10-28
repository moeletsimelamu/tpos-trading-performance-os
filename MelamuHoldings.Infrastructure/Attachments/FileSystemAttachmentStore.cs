using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MelamuHoldings.Application.Trading;

namespace MelamuHoldings.Infrastructure.Attachments;

// Keeps attachment files in one folder outside the web root (by default App_Data/attachments under the API's
// content root). Files get random names; a key is accepted only in that exact shape, so it can never point
// outside the folder.
public partial class FileSystemAttachmentStore : IAttachmentStore
{
    private readonly string _root;

    public FileSystemAttachmentStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(byte[] content, string extension)
    {
        var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)) + extension;
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException($"Unsupported extension '{extension}'.", nameof(extension));
        await File.WriteAllBytesAsync(PathOf(key), content);
        return key;
    }

    public Stream? Open(string storageKey)
    {
        var path = PathOf(storageKey);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
    }

    // A file already gone is fine; one that cannot be removed now is left rather than failing the request
    public void Delete(string storageKey)
    {
        try { File.Delete(PathOf(storageKey)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string PathOf(string storageKey)
    {
        if (!KeyPattern().IsMatch(storageKey)) throw new ArgumentException("Invalid attachment key.", nameof(storageKey));
        return Path.Combine(_root, storageKey);
    }

    [GeneratedRegex(@"^[0-9a-f]{32}\.(png|jpg|webp|pdf)$")]
    private static partial Regex KeyPattern();
}
