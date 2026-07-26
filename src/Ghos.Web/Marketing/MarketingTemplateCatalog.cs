namespace Ghos.Web.Marketing;

public sealed record MarketingTemplateDefinition(
    string Key,
    string Name,
    string Format,
    int Width,
    int Height,
    string Purpose);

public static class MarketingTemplateCatalog
{
    public const string MaterialMonday = "material-monday";

    public static readonly IReadOnlyList<MarketingTemplateDefinition> All =
    [
        new(
            "material-monday-square",
            "Material Monday Fact Card",
            "Square post",
            1080,
            1080,
            "Facebook and Instagram feed"),
        new(
            "material-monday-story",
            "Material Monday Story",
            "Vertical story",
            1080,
            1920,
            "Facebook and Instagram stories"),
        new(
            "material-monday-reel-cover",
            "Material Monday Reel Cover",
            "Vertical cover",
            1080,
            1920,
            "Instagram Reels and short video")
    ];
}
