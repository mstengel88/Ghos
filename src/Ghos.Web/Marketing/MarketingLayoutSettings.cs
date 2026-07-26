using System.Text.Json;

namespace Ghos.Web.Marketing;

public static class MarketingLayoutElementKeys
{
    public const string BackgroundImage = "background-image";
    public const string AlternateName = "alternate-name";
    public const string Headline = "headline";
    public const string Subheadline = "subheadline";
    public const string Facts = "facts";

    public static readonly IReadOnlyList<string> All =
    [
        AlternateName,
        Headline,
        Subheadline,
        Facts
    ];

    public static string GetLabel(string key, bool vertical) =>
        key switch
        {
            AlternateName => "Alternate name",
            Headline => "Headline",
            Subheadline => "Subheadline",
            Facts when vertical => "Call to action",
            Facts => "Fact items",
            _ => "Text block"
        };
}

public sealed class MarketingLayoutSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public Dictionary<string, Dictionary<string, MarketingElementLayout>> Templates
    {
        get;
        set;
    } = new(StringComparer.OrdinalIgnoreCase);

    public MarketingElementLayout GetOrCreate(
        string templateKey,
        string elementKey)
    {
        if (!Templates.TryGetValue(templateKey, out var elements))
        {
            elements = new Dictionary<string, MarketingElementLayout>(
                StringComparer.OrdinalIgnoreCase);
            Templates[templateKey] = elements;
        }

        if (!elements.TryGetValue(elementKey, out var layout))
        {
            layout = new MarketingElementLayout();
            elements[elementKey] = layout;
        }

        return layout;
    }

    public static MarketingLayoutSettings Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new MarketingLayoutSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<MarketingLayoutSettings>(
                value,
                SerializerOptions) ?? new MarketingLayoutSettings();
        }
        catch (JsonException)
        {
            return new MarketingLayoutSettings();
        }
    }

    public string ToJson() =>
        JsonSerializer.Serialize(this, SerializerOptions);
}

public sealed class MarketingElementLayout
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Scale { get; set; } = 1;

    public void Normalize()
    {
        X = Math.Clamp(X, -600, 600);
        Y = Math.Clamp(Y, -900, 900);
        Scale = Math.Clamp(Scale, .45, 1.75);
    }

    public void NormalizeImage(int canvasWidth, int canvasHeight)
    {
        Scale = Math.Clamp(Scale, 1, 2.5);
        var maximumX = (Scale - 1) * canvasWidth / 2;
        var maximumY = (Scale - 1) * canvasHeight / 2;
        X = Math.Clamp(X, -maximumX, maximumX);
        Y = Math.Clamp(Y, -maximumY, maximumY);
    }
}
