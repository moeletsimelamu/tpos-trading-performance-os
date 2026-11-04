using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

public static class AuditLog
{
    // Enums by name, so the log still reads correctly if an enum is ever reordered
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // For summaries: the same on every server, whatever its culture's decimal separator
    public static string Amount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static AuditEvent Event(DateTime at, int userId, string action, string entityType, object entityId, string summary, object? data = null) =>
        new(at, userId, action, entityType, entityId.ToString()!, Truncate(summary, 300), data is null ? null : JsonSerializer.Serialize(data, Json));

    public static async Task<PagedResult<AuditEventDto>> ListAsync(IFinanceRepository repository, int page, int pageSize)
    {
        var events = await repository.GetAuditEventsAsync(page, pageSize);
        return new PagedResult<AuditEventDto>(
            events.Items.Select(e => new AuditEventDto(e.Id, e.OccurredAt, e.UserId, e.Action, e.EntityType, e.EntityId, e.Summary, e.Data)).ToList(),
            events.Page, events.PageSize, events.TotalCount);
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
}
