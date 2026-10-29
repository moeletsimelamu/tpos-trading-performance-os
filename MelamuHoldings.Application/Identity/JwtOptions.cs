using System.Text;

namespace MelamuHoldings.Application.Identity;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinimumKeyBytes = 32;

    // Supplied by user-secrets (development) or the Jwt__Key environment variable — never appsettings.json
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiryHours { get; set; } = 8;

    public bool IsValid() =>
        Encoding.UTF8.GetByteCount(Key) >= MinimumKeyBytes &&
        !string.IsNullOrWhiteSpace(Issuer) &&
        !string.IsNullOrWhiteSpace(Audience) &&
        ExpiryHours > 0;
}
