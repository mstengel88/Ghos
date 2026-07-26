using Ghos.Web.Data;

namespace Ghos.Web.Marketing;

public sealed record MarketingReadinessItem(
    string Label,
    bool IsReady);

public sealed class MarketingReadinessResult
{
    public required IReadOnlyList<MarketingReadinessItem> Items { get; init; }

    public bool IsReady => Items.All(item => item.IsReady);

    public int CompletedCount => Items.Count(item => item.IsReady);
}

public static class MarketingReadiness
{
    public static MarketingReadinessResult Evaluate(
        MarketingContentPackage content)
    {
        var hasFacts = SplitLines(content.FactItems).Count > 0;
        var hasApprovedImage = content.DigitalAsset is
        {
            Kind: AssetKind.Image,
            Status: AssetStatus.Approved
        };
        var hasValidDestination = Uri.TryCreate(
            content.DestinationUrl,
            UriKind.Absolute,
            out var destination) &&
            destination.Scheme is "http" or "https";

        return new MarketingReadinessResult
        {
            Items =
            [
                new(
                    "Verified product is linked",
                    content.ProductId is not null),
                new(
                    "Approved primary image",
                    hasApprovedImage),
                new(
                    "Headline, description, facts, and call to action",
                    HasText(content.Headline) &&
                    HasText(content.Subheadline) &&
                    hasFacts &&
                    HasText(content.CallToAction)),
                new(
                    "Facebook and Instagram captions",
                    HasText(content.FacebookCaption) &&
                    HasText(content.InstagramCaption)),
                new(
                    "Story prompt and Reel plan",
                    HasText(content.StoryPrompt) &&
                    HasText(content.ReelScript)),
                new(
                    "Valid product destination link",
                    hasValidDestination),
                new(
                    "Planned publication time",
                    content.ScheduledForUtc is not null)
            ]
        };
    }

    private static bool HasText(string? value) =>
        !string.IsNullOrWhiteSpace(value);

    private static IReadOnlyList<string> SplitLines(string? value) =>
        (value ?? string.Empty)
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
}
