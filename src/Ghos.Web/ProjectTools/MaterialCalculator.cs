using System.Text.RegularExpressions;
using Ghos.Web.Data;

namespace Ghos.Web.ProjectTools;

public enum ProjectShape
{
    Rectangle,
    Circle,
    Area
}

public enum MaterialAreaUnit
{
    SquareFeet,
    SquareInches
}

public sealed record MaterialCalculation(
    decimal BaseCubicYards,
    decimal RecommendedCubicYards,
    decimal OrderQuantity,
    string UnitLabel);

public sealed record MaterialConversion(
    string Category,
    string Name,
    string? ShopifyHandle,
    decimal? TonsPerCubicYard,
    MaterialSoldBy SoldBy,
    string? VariantHint = null)
{
    public string UnitLabel => SoldBy == MaterialSoldBy.Ton ? "tons" : "cubic yards";
}

public sealed record MaterialConversionGroup(
    string Category,
    IReadOnlyList<MaterialConversion> Materials);

public static partial class MaterialCalculator
{
    public static MaterialCalculation Calculate(
        ProjectShape shape,
        decimal lengthFeet,
        decimal widthFeet,
        decimal diameterFeet,
        decimal areaValue,
        MaterialAreaUnit areaUnit,
        decimal depthInches,
        decimal manualCubicYards,
        decimal extraPercent,
        MaterialConversion? material)
    {
        var baseYards = manualCubicYards > 0
            ? manualCubicYards
            : shape switch
            {
                ProjectShape.Circle when diameterFeet > 0 && depthInches > 0 =>
                    (decimal)Math.PI * diameterFeet * diameterFeet * depthInches / 1296m,
                ProjectShape.Area when areaValue > 0 && depthInches > 0 =>
                    ConvertToSquareFeet(areaValue, areaUnit) * depthInches / 324m,
                _ when lengthFeet > 0 && widthFeet > 0 && depthInches > 0 =>
                    lengthFeet * widthFeet * depthInches / 324m,
                _ => 0m
            };

        var recommendedYards =
            Math.Max(0m, baseYards) *
            (1m + Math.Max(0m, extraPercent) / 100m);
        var rawOrderQuantity = material?.SoldBy == MaterialSoldBy.Ton
            ? recommendedYards * (material.TonsPerCubicYard ?? 1m)
            : recommendedYards;
        var orderQuantity = rawOrderQuantity <= 0
            ? 0m
            : Math.Ceiling(rawOrderQuantity);

        return new MaterialCalculation(
            Math.Round(baseYards, 2),
            Math.Round(recommendedYards, 2),
            orderQuantity,
            material?.UnitLabel ?? "cubic yards");
    }

    private static decimal ConvertToSquareFeet(
        decimal areaValue,
        MaterialAreaUnit areaUnit) =>
        areaUnit == MaterialAreaUnit.SquareInches
            ? areaValue / 144m
            : areaValue;

    public static MaterialConversion? Find(
        string? title,
        string? handle,
        string? variantTitle = null)
    {
        var normalizedTitle = Normalize($"{title} {variantTitle}");
        var normalizedHandle = Normalize(handle);
        var matches = All
            .Where(material =>
                !string.IsNullOrWhiteSpace(normalizedHandle) &&
                Normalize(material.ShopifyHandle) == normalizedHandle)
            .ToList();

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            var sized = matches.FirstOrDefault(material =>
                !string.IsNullOrWhiteSpace(material.VariantHint) &&
                normalizedTitle.Contains(
                    Normalize(material.VariantHint),
                    StringComparison.Ordinal));
            return sized ?? matches[0];
        }

        return All.FirstOrDefault(material =>
        {
            var materialName = Normalize(material.Name);
            return materialName == normalizedTitle ||
                normalizedTitle.Contains(materialName, StringComparison.Ordinal) ||
                materialName.Contains(normalizedTitle, StringComparison.Ordinal);
        });
    }

    public static IReadOnlyList<MaterialConversionGroup> OrderedGroups(
        MaterialConversion? selected)
    {
        if (selected is null)
        {
            return Groups;
        }

        return Groups
            .OrderBy(group =>
                group.Category.Equals(selected.Category, StringComparison.Ordinal)
                    ? 0
                    : 1)
            .ThenBy(group => Array.IndexOf(
                Groups.Select(item => item.Category).ToArray(),
                group.Category))
            .ToList();
    }

    private static string Normalize(string? value) =>
        WhitespaceRegex().Replace(
            NonMaterialCharactersRegex().Replace(
                (value ?? string.Empty)
                    .ToLowerInvariant()
                    .Replace("&", " and ", StringComparison.Ordinal),
                " "),
            " ").Trim();

    [GeneratedRegex(@"[^a-z0-9#]+")]
    private static partial Regex NonMaterialCharactersRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private static MaterialConversion Yard(
        string category,
        string name,
        string handle) =>
        new(category, name, handle, null, MaterialSoldBy.CubicYard);

    private static MaterialConversion Ton(
        string category,
        string name,
        string? handle,
        decimal? tonsPerYard,
        string? variantHint = null) =>
        new(category, name, handle, tonsPerYard, MaterialSoldBy.Ton, variantHint);

    public static readonly IReadOnlyList<MaterialConversionGroup> Groups =
    [
        new("Mulch",
        [
            Yard("Mulch", "Red Environmental Mulch", "red-enviromental-mulch"),
            Yard("Mulch", "Midnight Black Mulch", "black-mulch"),
            Yard("Mulch", "Cedar Mulch", "cedar-mulch"),
            Yard("Mulch", "Certified Playground Chips", "certified-playground-chips"),
            Yard("Mulch", "Deep Brown Mulch", "deep-brown-mulch"),
            Yard("Mulch", "Hemlock Mulch", "hemlock-mulch"),
            Yard("Mulch", "Hardwood Blend", "hardwood-blend"),
            Yard("Mulch", "Premium Blend Mulch", "premium-blend-mulch"),
            Yard("Mulch", "Cocoa Bean Mulch", "cocoa-bean-mulch")
        ]),
        new("Soil",
        [
            Yard("Soil", "Lawn & Garden Topsoil", "top-soil"),
            Yard("Soil", "Composted Soil", "composted-soil"),
            Yard("Soil", "Compost & Topsoil Mix", "compost-topsoil-mix"),
            Yard("Soil", "Custom Sand Soil Blend", "custom-sand-soil-blend"),
            Yard("Soil", "Premium Garden Mix", "premium-garden-mix-1")
        ]),
        new("Aggregate",
        [
            Ton("Aggregate", "Screenings", "screenings", 1.3m),
            Ton("Aggregate", "3/8\" Base", "3-8-base", 1.3m),
            Ton("Aggregate", "3/4\" Base", "3-4-base", 1.2m),
            Ton("Aggregate", "1.25\" Base", "1-25-base", 1.26m),
            Ton("Aggregate", "3/8 Chips", "3-8-chips", 1m),
            Ton("Aggregate", "#1 Stone", "1-stone", 1.04m),
            Ton("Aggregate", "#2 Stone", "2-stone", 1.16m),
            Ton("Aggregate", "#3 Stone", "3-stone", 1.1m),
            Ton("Aggregate", "4-8\" Stone", "4-8-stone", 1.2m)
        ]),
        new("Sand",
        [
            Ton("Sand", "Ultra Fine Washed Sand", "ultra-fine-washed-sand", 1.2m),
            Ton("Sand", "Mason Sand", "mason-sand", 1.2m),
            Ton("Sand", "Coarse Torpedo Sand", "coarse-torpedo-sand", 1.2m),
            Ton("Sand", "Bedding Sand", "bedding-sand", 1.3m)
        ]),
        new("Decorative Landscape Stone",
        [
            Ton("Decorative Landscape Stone", "Medium Alpine Stone", "alpine-stone", 1.18m, "Medium"),
            Ton("Decorative Landscape Stone", "Large Alpine Stone", "alpine-stone", 1.04m, "Large"),
            Ton("Decorative Landscape Stone", "#2 Landscape Stone", "2-landscape-stone", 1.15m),
            Ton("Decorative Landscape Stone", "#3 Landscape Stone", "3-washed-stone", 1.1m),
            Ton("Decorative Landscape Stone", "Black Raven Sand", "black-raven-sand", 1.18m),
            Ton("Decorative Landscape Stone", "3/8\" Black Raven", "3-8-black-raven", 1.25m),
            Ton("Decorative Landscape Stone", "3/4\" Black Raven", "3-4-black-raven", 1.1m),
            Ton("Decorative Landscape Stone", "Decorative Black Raven", "decorative-black-raven", 1.25m),
            Ton("Decorative Landscape Stone", "Red Pepper", null, null),
            Ton("Decorative Landscape Stone", "Medium Blue Basin", "blue-basin", 1.14m, "Medium"),
            Ton("Decorative Landscape Stone", "Large Blue Basin", "blue-basin", 1.1m, "Large"),
            Ton("Decorative Landscape Stone", "Red Spardust", "red-spardust", 1.2m),
            Ton("Decorative Landscape Stone", "Gray Spardust", "gray-spardust", 1.5m),
            Ton("Decorative Landscape Stone", "Shooting Star Spardust", "shooting-star-spardust", 1.5m),
            Ton("Decorative Landscape Stone", "Pea Gravel", "pea-gravel", 1.05m),
            Ton("Decorative Landscape Stone", "Medium American Heritage", "american-heritage", 1.14m, "Medium"),
            Ton("Decorative Landscape Stone", "Large American Heritage", "american-heritage", 1.18m, "Large"),
            Ton("Decorative Landscape Stone", "Medium Mississippi Stone", "mississippi-stone", 1.14m, "Medium"),
            Ton("Decorative Landscape Stone", "Large Mississippi Stone", "mississippi-stone", 1.18m, "Large")
        ])
    ];

    public static readonly IReadOnlyList<MaterialConversion> All =
        Groups.SelectMany(group => group.Materials).ToList();
}

public static class GreenHillsMaterialProfiles
{
    public static readonly IReadOnlyDictionary<string, (MaterialSoldBy SoldBy, decimal? TonsPerYard)>
        ByShopifyHandle = MaterialCalculator.All
            .Where(material => !string.IsNullOrWhiteSpace(material.ShopifyHandle))
            .GroupBy(material => material.ShopifyHandle!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (
                    group.First().SoldBy,
                    group.First().TonsPerCubicYard),
                StringComparer.OrdinalIgnoreCase);
}
