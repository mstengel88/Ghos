using Ghos.Web.Data;

namespace Ghos.Web.ProjectTools;

public enum ProjectShape
{
    Rectangle,
    Circle,
    Manual
}

public sealed record MaterialCalculation(
    decimal BaseCubicYards,
    decimal RecommendedCubicYards,
    decimal OrderQuantity,
    string UnitLabel);

public static class MaterialCalculator
{
    public static MaterialCalculation Calculate(
        ProjectShape shape,
        decimal lengthFeet,
        decimal widthFeet,
        decimal diameterFeet,
        decimal depthInches,
        decimal manualCubicYards,
        decimal extraPercent,
        MaterialSoldBy soldBy,
        decimal? tonsPerCubicYard,
        decimal orderIncrement)
    {
        var baseYards = shape switch
        {
            ProjectShape.Circle when diameterFeet > 0 && depthInches > 0 =>
                (decimal)Math.PI * diameterFeet * diameterFeet * depthInches / 1296m,
            ProjectShape.Manual when manualCubicYards > 0 => manualCubicYards,
            _ when lengthFeet > 0 && widthFeet > 0 && depthInches > 0 =>
                lengthFeet * widthFeet * depthInches / 324m,
            _ => 0m
        };

        var recommendedYards =
            baseYards * (1m + Math.Max(0m, extraPercent) / 100m);
        var rawOrderQuantity = soldBy == MaterialSoldBy.Ton
            ? recommendedYards * (tonsPerCubicYard ?? 1m)
            : recommendedYards;
        var increment = orderIncrement > 0 ? orderIncrement : 1m;
        var orderQuantity = rawOrderQuantity <= 0
            ? 0
            : Math.Ceiling(rawOrderQuantity / increment) * increment;

        return new MaterialCalculation(
            Math.Round(baseYards, 2),
            Math.Round(recommendedYards, 2),
            Math.Round(orderQuantity, 2),
            soldBy switch
            {
                MaterialSoldBy.CubicYard => "cubic yards",
                MaterialSoldBy.Ton => "tons",
                _ => "units"
            });
    }
}

public static class GreenHillsMaterialProfiles
{
    public static readonly IReadOnlyDictionary<string, (MaterialSoldBy SoldBy, decimal? TonsPerYard)>
        ByShopifyHandle =
            new Dictionary<string, (MaterialSoldBy, decimal?)>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["red-enviromental-mulch"] = (MaterialSoldBy.CubicYard, null),
                ["black-mulch"] = (MaterialSoldBy.CubicYard, null),
                ["cedar-mulch"] = (MaterialSoldBy.CubicYard, null),
                ["certified-playground-chips"] = (MaterialSoldBy.CubicYard, null),
                ["deep-brown-mulch"] = (MaterialSoldBy.CubicYard, null),
                ["hemlock-mulch"] = (MaterialSoldBy.CubicYard, null),
                ["hardwood-blend"] = (MaterialSoldBy.CubicYard, null),
                ["premium-blend-mulch"] = (MaterialSoldBy.CubicYard, null),
                ["cocoa-bean-mulch"] = (MaterialSoldBy.CubicYard, null),
                ["top-soil"] = (MaterialSoldBy.CubicYard, null),
                ["composted-soil"] = (MaterialSoldBy.CubicYard, null),
                ["compost-topsoil-mix"] = (MaterialSoldBy.CubicYard, null),
                ["custom-sand-soil-blend"] = (MaterialSoldBy.CubicYard, null),
                ["premium-garden-mix-1"] = (MaterialSoldBy.CubicYard, null),
                ["screenings"] = (MaterialSoldBy.Ton, 1.3m),
                ["3-8-base"] = (MaterialSoldBy.Ton, 1.3m),
                ["3-4-base"] = (MaterialSoldBy.Ton, 1.2m),
                ["1-25-base"] = (MaterialSoldBy.Ton, 1.26m),
                ["3-8-chips"] = (MaterialSoldBy.Ton, 1m),
                ["1-stone"] = (MaterialSoldBy.Ton, 1.04m),
                ["2-stone"] = (MaterialSoldBy.Ton, 1.16m),
                ["3-stone"] = (MaterialSoldBy.Ton, 1.1m),
                ["4-8-stone"] = (MaterialSoldBy.Ton, 1.2m),
                ["ultra-fine-washed-sand"] = (MaterialSoldBy.Ton, 1.2m),
                ["mason-sand"] = (MaterialSoldBy.Ton, 1.2m),
                ["coarse-torpedo-sand"] = (MaterialSoldBy.Ton, 1.2m),
                ["bedding-sand"] = (MaterialSoldBy.Ton, 1.3m),
                ["2-landscape-stone"] = (MaterialSoldBy.Ton, 1.15m),
                ["3-washed-stone"] = (MaterialSoldBy.Ton, 1.1m),
                ["black-raven-sand"] = (MaterialSoldBy.Ton, 1.18m),
                ["3-8-black-raven"] = (MaterialSoldBy.Ton, 1.25m),
                ["3-4-black-raven"] = (MaterialSoldBy.Ton, 1.1m),
                ["decorative-black-raven"] = (MaterialSoldBy.Ton, 1.25m),
                ["blue-basin"] = (MaterialSoldBy.Ton, 1.14m),
                ["red-spardust"] = (MaterialSoldBy.Ton, 1.2m),
                ["gray-spardust"] = (MaterialSoldBy.Ton, 1.5m),
                ["shooting-star-spardust"] = (MaterialSoldBy.Ton, 1.5m),
                ["pea-gravel"] = (MaterialSoldBy.Ton, 1.05m),
                ["american-heritage"] = (MaterialSoldBy.Ton, 1.14m),
                ["mississippi-stone"] = (MaterialSoldBy.Ton, 1.14m)
            };
}
