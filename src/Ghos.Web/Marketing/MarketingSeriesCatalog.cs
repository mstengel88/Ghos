using Ghos.Web.Data;

namespace Ghos.Web.Marketing;

public sealed record MarketingSeriesDefinition(
    string Key,
    string Name,
    DayOfWeek PublishDay,
    int PublishHour,
    string Purpose,
    string Prompt,
    string Accent);

public static class MarketingSeriesCatalog
{
    public static readonly IReadOnlyList<MarketingSeriesDefinition> All =
    [
        new(
            "material-monday",
            "Material Monday",
            DayOfWeek.Monday,
            8,
            "Teach customers about one verified product and its best uses.",
            "Choose a product the team wants customers to understand.",
            "lime"),
        new(
            "ask-green-hills",
            "Ask Green Hills",
            DayOfWeek.Tuesday,
            9,
            "Answer a real buying or project-planning question.",
            "Choose the product behind a frequent customer question.",
            "blue"),
        new(
            "project-spotlight",
            "Project Spotlight",
            DayOfWeek.Wednesday,
            11,
            "Turn a product into project inspiration and a customer story.",
            "Choose the product featured in the completed project.",
            "orange"),
        new(
            "inside-green-hills",
            "Inside Green Hills",
            DayOfWeek.Thursday,
            10,
            "Show the people, equipment, and process behind dependable service.",
            "Choose a product to anchor the behind-the-scenes story.",
            "charcoal"),
        new(
            "weekend-ready",
            "Weekend Ready",
            DayOfWeek.Friday,
            8,
            "Help customers prepare for a practical weekend project.",
            "Choose the product customers should plan or order before the weekend.",
            "red")
    ];

    public static MarketingSeriesDefinition Get(string key) =>
        All.Single(item =>
            item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static MarketingContentPackage CreateDraft(
        MarketingSeriesDefinition series,
        Product product,
        DateTime scheduledForUtc,
        string? userId)
    {
        var localDate = TimeZoneInfo.ConvertTimeFromUtc(
            scheduledForUtc,
            TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));
        var facts = BuildVerifiedFacts(product);
        var description = GetVerifiedDescription(product);
        var destinationUrl = GetProductUrl(product);
        var primaryAsset = product.AssetLinks
            .Where(link =>
                link.IsPrimary &&
                link.DigitalAsset.Kind == AssetKind.Image &&
                link.DigitalAsset.Status == AssetStatus.Approved)
            .Select(link => link.DigitalAsset)
            .FirstOrDefault();
        var alternateName = product.AlternateNames.Count == 0
            ? null
            : $"Also known as {product.AlternateNames
                .OrderBy(item => item.Name)
                .First().Name}";
        var now = DateTime.UtcNow;

        return new MarketingContentPackage
        {
            Slug = $"{series.Key}-{product.Slug}-{localDate:yyyy-MM-dd}",
            Title = $"{series.Name} — {product.Name}",
            Series = series.Name,
            TemplateKey = series.Key,
            Status = MarketingContentStatus.Draft,
            ScheduledForUtc = scheduledForUtc,
            ProductId = product.Id,
            DigitalAssetId = primaryAsset?.Id,
            Headline = BuildHeadline(series.Key, product),
            AlternateName = alternateName,
            Subheadline = BuildSubheadline(series.Key, product, description),
            FactItems = string.Join(Environment.NewLine, facts),
            FacebookCaption = BuildFacebookDraft(
                series.Key,
                product,
                description,
                facts,
                destinationUrl),
            InstagramCaption = BuildInstagramDraft(
                series.Key,
                product,
                description,
                facts),
            StoryPrompt = BuildStoryPrompt(series.Key, product),
            ReelScript = BuildReelDraft(series.Key, product),
            Hashtags = BuildHashtags(series, product),
            CallToAction = BuildCallToAction(series.Key),
            DestinationUrl = destinationUrl,
            LayoutSettingsJson = "{}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
    }

    public static IReadOnlyList<string> BuildVerifiedFacts(Product product)
    {
        var uses = (product.BestUses ?? string.Empty)
            .Split(
                ['\r', '\n', ',', ';'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Take(4)
            .ToList();
        if (uses.Count > 0)
        {
            return uses;
        }

        var facts = new List<string>();
        if (product.AvailableForPickup)
        {
            facts.Add("Pickup available");
        }
        if (product.AvailableForDelivery)
        {
            facts.Add("Delivery available");
        }
        if (product.AvailableInBulk)
        {
            facts.Add("Bulk material");
        }
        if (product.AvailableBagged)
        {
            facts.Add("Bagged option");
        }
        return facts.Count > 0 ? facts : ["Ask our team for details"];
    }

    private static string GetVerifiedDescription(Product product)
    {
        var value = product.ShortDescription
            ?? product.ShopifySeoDescription
            ?? product.ShopifyProductType
            ?? "Available from Green Hills Supply";
        return value.Length <= 180 ? value : $"{value[..177].TrimEnd()}…";
    }

    private static string BuildHeadline(string key, Product product) =>
        key switch
        {
            "ask-green-hills" => $"ASK ABOUT {product.Name}".ToUpperInvariant(),
            "project-spotlight" => $"BUILT WITH {product.Name}".ToUpperInvariant(),
            "inside-green-hills" => "INSIDE GREEN HILLS",
            "weekend-ready" => $"{product.Name} THIS WEEKEND".ToUpperInvariant(),
            _ => product.Name.ToUpperInvariant()
        };

    private static string BuildSubheadline(
        string key,
        Product product,
        string description) =>
        key switch
        {
            "ask-green-hills" =>
                $"What should you know before choosing {product.Name}? {description}",
            "project-spotlight" =>
                $"Show customers what a well-planned project using {product.Name} can become.",
            "inside-green-hills" =>
                $"See how {product.Name} moves from our yard to a customer's project.",
            "weekend-ready" =>
                $"Planning a weekend project? Start with the right amount of {product.Name}.",
            _ => description
        };

    private static string BuildFacebookDraft(
        string key,
        Product product,
        string description,
        IReadOnlyList<string> facts,
        string destinationUrl)
    {
        var introduction = key switch
        {
            "ask-green-hills" =>
                $"❓ Ask Green Hills: Is {product.Name} right for your project?",
            "project-spotlight" =>
                $"🏡 Project Spotlight: Built with {product.Name}",
            "inside-green-hills" =>
                $"🚜 Inside Green Hills: A closer look at {product.Name}",
            "weekend-ready" =>
                $"✅ Weekend Ready: Planning to use {product.Name}?",
            _ => $"🪨 Material Monday: {product.Name}"
        };
        return $"""
            {introduction}

            {description}

            Verified highlights:
            {string.Join(Environment.NewLine, facts.Select(fact => $"✔ {fact}"))}

            Tell us about your project and our team will help you confirm the right material and quantity.

            Learn more: {destinationUrl}
            """;
    }

    private static string BuildInstagramDraft(
        string key,
        Product product,
        string description,
        IReadOnlyList<string> facts)
    {
        var introduction = key switch
        {
            "ask-green-hills" => $"Ask Green Hills ❓ {product.Name}",
            "project-spotlight" => $"Project Spotlight 🏡 {product.Name}",
            "inside-green-hills" => $"Inside Green Hills 🚜 {product.Name}",
            "weekend-ready" => $"Weekend Ready ✅ {product.Name}",
            _ => $"Material Monday 🪨 {product.Name}"
        };
        return $"""
            {introduction}

            {description}

            {string.Join(" · ", facts)}

            Planning a project? Our team can help you choose the right material and estimate how much you need.

            Learn more at the link in our bio.
            """;
    }

    private static string BuildStoryPrompt(string key, Product product) =>
        key switch
        {
            "ask-green-hills" =>
                $"What would you like to know about {product.Name}? | Best uses | How much do I need?",
            "project-spotlight" =>
                $"Would you use {product.Name} in your next project? | Yes | Show me more",
            "inside-green-hills" =>
                $"Want to see more behind the scenes? | Yes | Absolutely",
            "weekend-ready" =>
                $"Working outside this weekend? | Yes | Still planning",
            _ =>
                $"Have a question about {product.Name}? | Yes | Tell me more"
        };

    private static string BuildReelDraft(string key, Product product)
    {
        var hook = key switch
        {
            "ask-green-hills" =>
                $"“Is {product.Name} the right choice for your project?”",
            "project-spotlight" =>
                $"“See what this project became with {product.Name}.”",
            "inside-green-hills" =>
                $"“Here is how {product.Name} moves through our yard.”",
            "weekend-ready" =>
                $"“Planning to use {product.Name} this weekend?”",
            _ => $"“What should you know about {product.Name}?”"
        };
        return $"""
            HOOK — On-screen text: {hook}

            SHOW — Use the approved product image, then add fresh yard, loading, delivery, or completed-project footage.

            TEACH — Share one verified use, one planning tip, and one mistake customers can avoid.

            CLOSE — On-screen text: “{product.Name} · Green Hills Supply”

            CTA — “Tell us about your project and we will help you plan it.”
            """;
    }

    private static string BuildCallToAction(string key) =>
        key switch
        {
            "ask-green-hills" => "Ask our team before you order",
            "project-spotlight" => "Start planning your project",
            "inside-green-hills" => "See how Green Hills makes projects easier",
            "weekend-ready" => "Get your weekend project ready",
            _ => "Plan your project with Green Hills Supply"
        };

    private static string BuildHashtags(
        MarketingSeriesDefinition series,
        Product product)
    {
        var productTag = new string(
            product.Name.Where(char.IsLetterOrDigit).ToArray());
        var seriesTag = new string(
            series.Name.Where(char.IsLetterOrDigit).ToArray());
        return
            $"#GreenHillsSupply #{seriesTag} #{productTag} #LandscapeSupply #WisconsinLandscaping";
    }

    private static string GetProductUrl(Product product)
    {
        var handle = product.ShopifyHandle ?? product.Slug;
        return $"https://greenhillssupply.com/products/{handle}";
    }
}
