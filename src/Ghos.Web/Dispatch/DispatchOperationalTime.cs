using System.Globalization;
using Ghos.Web.Data;

namespace Ghos.Web.Dispatch;

public static class DispatchOperationalTime
{
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "M/d/yyyy",
        "MM/dd/yyyy"
    ];

    private static readonly TimeZoneInfo BusinessTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    public static DateOnly Today =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow,
                BusinessTimeZone));

    public static bool IsCurrentWork(Delivery delivery)
    {
        if (delivery.Status is DeliveryStatus.Delivered or
            DeliveryStatus.Cancelled)
        {
            return false;
        }

        if (delivery.Status is DeliveryStatus.EnRoute or
            DeliveryStatus.Arrived or
            DeliveryStatus.Issue)
        {
            return true;
        }

        return delivery.ScheduledForUtc is null ||
            DateOnly.FromDateTime(delivery.ScheduledForUtc.Value) >= Today;
    }

    public static bool IsCurrentEnRoute(DispatchExportOrder order)
    {
        if (!order.DeliveryStatus.Equals(
                "en_route",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var today = Today;
        return ParseBusinessDate(order.RequestedWindow) == today &&
            ParseBusinessDate(order.DepartedAt) == today;
    }

    public static DateOnly? ParseBusinessDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (DateOnly.TryParseExact(
                trimmed,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date;
        }

        return DateTimeOffset.TryParse(
            trimmed,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var timestamp)
            ? DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(
                    timestamp,
                    BusinessTimeZone).DateTime)
            : null;
    }
}
