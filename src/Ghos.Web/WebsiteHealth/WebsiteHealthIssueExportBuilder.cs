using System.Globalization;
using System.Text;

namespace Ghos.Web.WebsiteHealth;

internal static class WebsiteHealthIssueExportBuilder
{
    internal static string BuildCsv(
        IEnumerable<WebsiteHealthIssue> issues)
    {
        var builder = new StringBuilder();
        builder.Append('\uFEFF');
        AppendRow(
            builder,
            "Finding type",
            "Severity",
            "Status",
            "Page URL",
            "Current value",
            "Current characters",
            "Suggested value",
            "Suggested characters",
            "Reviewed working copy",
            "Reviewed characters",
            "Reviewed at",
            "Shopify location",
            "Official instructions",
            "Triage note");
        foreach (var issue in issues
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.CheckKey)
            .ThenBy(issue => issue.AffectedUrl))
        {
            AppendRow(
                builder,
                FormatCheckKey(issue.CheckKey),
                issue.Severity.ToString(),
                GetStatus(issue),
                issue.AffectedUrl,
                issue.CurrentValue,
                CharacterCount(issue.CurrentValue),
                issue.SuggestedValue,
                CharacterCount(issue.SuggestedValue),
                issue.ReviewedValue,
                CharacterCount(issue.ReviewedValue),
                issue.ReviewedAtUtc,
                issue.FixLocation,
                issue.FixDocumentationUrl,
                issue.TriageNote);
        }

        return builder.ToString();
    }

    private static void AppendRow(
        StringBuilder builder,
        params object?[] values)
    {
        builder.AppendJoin(',', values.Select(FormatCell));
        builder.AppendLine();
    }

    private static string FormatCell(object? value)
    {
        var text = Convert.ToString(
                value,
                CultureInfo.InvariantCulture) ??
            string.Empty;
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.StartsWith('=') ||
            text.StartsWith('+') ||
            text.StartsWith('-') ||
            text.StartsWith('@'))
        {
            text = $"'{text}";
        }

        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static object? CharacterCount(string? value) =>
        value is null ? null : value.Length;

    private static string GetStatus(WebsiteHealthIssue issue) =>
        issue.ResolvedAtUtc is not null ? "Resolved" :
        issue.SuppressedAtUtc is not null ? "Suppressed" :
        issue.AcknowledgedAtUtc is not null ? "Acknowledged" :
        "Open";

    private static string FormatCheckKey(string key) => key switch
    {
        "image-alt" => "Missing image alt text",
        "meta-description" => "Missing meta description",
        "meta-description-length" => "Meta description length",
        "duplicate-meta-description" => "Duplicate meta description",
        "title" => "Missing page title",
        "title-length" => "Page title length",
        "duplicate-title" => "Duplicate page title",
        "canonical" => "Missing canonical URL",
        "schema" => "Missing structured data",
        "internal-link" => "Broken internal link",
        "ssl" => "SSL certificate",
        _ => key.Replace('-', ' ')
    };
}
