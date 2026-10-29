using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MelamuHoldings.Application.Finance;

// Shared rules for requests made safe to retry with an Idempotency-Key
public static class Idempotency
{
    // Without a client key a request still gets a unique one, so it takes effect exactly once
    public static string Key(string? clientKey) =>
        string.IsNullOrWhiteSpace(clientKey) ? Guid.NewGuid().ToString("N") : clientKey.Trim();

    // Identifies the request body so a reused key with a different payload is rejected rather than replayed
    public static string Hash(object request)
    {
        var payload = request.GetType().FullName + JsonSerializer.Serialize(request, request.GetType());
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    public const string KeyReusedMessage = "This Idempotency-Key was already used for a different request.";
}
