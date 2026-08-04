using System.Globalization;

namespace Ghos.Web.WebsiteHealth;

internal static class WebsiteHealthTimeFormatter
{
    private static readonly TimeZoneInfo CentralTime =
        TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    internal static string FormatTimestamp(DateTime? utc) =>
        utc is null
            ? "—"
            : FormatTimestamp(utc.Value);

    internal static string FormatTimestamp(DateTime utc)
    {
        var central = ToCentral(utc);
        var abbreviation = CentralTime.IsDaylightSavingTime(central)
            ? "CDT"
            : "CST";
        return $"{central.ToString(
            "MMM d, yyyy h:mm tt",
            CultureInfo.InvariantCulture)} {abbreviation}";
    }

    internal static string FormatDate(DateTime utc) =>
        ToCentral(utc).ToString(
            "MMM d",
            CultureInfo.InvariantCulture);

    internal static string FormatNumericDate(DateTime utc) =>
        ToCentral(utc).ToString(
            "M/d",
            CultureInfo.InvariantCulture);

    internal static DateTime ToCentral(DateTime utc)
    {
        var normalizedUtc = utc.Kind == DateTimeKind.Utc
            ? utc
            : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(
            normalizedUtc,
            CentralTime);
    }
}
