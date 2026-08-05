using System.Text.Json.Serialization;

namespace Ghos.Web.Dispatch;

public sealed record DispatchExportEnvelope(
    bool Ok,
    string? Message,
    string Version,
    DateTime GeneratedAt,
    string Cursor,
    int Count,
    bool HasMore,
    IReadOnlyList<DispatchExportOrder> Orders,
    IReadOnlyList<DispatchExportRoute> Routes,
    IReadOnlyList<string>? OpenOrderIds = null);

public sealed record DispatchExportOrder(
    string Id,
    string OrderNumber,
    string Customer,
    string Contact,
    string Address,
    string City,
    string Material,
    string Quantity,
    string Unit,
    string RequestedWindow,
    string TimePreference,
    string Status,
    string? AssignedRouteId,
    int? StopSequence,
    string DeliveryStatus,
    string? Eta,
    decimal? TravelMinutes,
    decimal? TravelMiles,
    string? DepartedAt,
    string? DeliveredAt,
    string? ProofName,
    string? ProofNotes,
    string CreatedAt,
    string UpdatedAt);

public sealed record DispatchExportRoute(
    string Id,
    string Code,
    string Truck,
    string Driver,
    string Helper,
    string Shift,
    string Region,
    bool IsActive,
    string UpdatedAt);
