using Ghos.Web.Data;

namespace Ghos.Web.ProjectTools;

public enum ProjectCalculatorKind
{
    Paver,
    Wall
}

public sealed record ProjectProductConfiguration(
    ProjectCalculatorKind Kind,
    decimal CoveragePerOrderUnitSqFt,
    string OrderUnitLabel,
    int? PiecesPerOrderUnit,
    decimal? UnitLengthInches,
    decimal? UnitHeightInches,
    int? LayersPerPallet,
    decimal? SquareFeetPerLayer,
    int? PalletWeightLbs,
    bool IsStandardDiscover);

public sealed record PaverWallCalculation(
    decimal BaseAreaSqFt,
    decimal RequiredAreaSqFt,
    decimal OrderQuantity,
    string OrderUnitLabel,
    decimal ProvidedCoverageSqFt,
    int EstimatedFullBlocks,
    int EstimatedCourses,
    int RecommendedPallets,
    int RecommendedLooseLayers,
    int RecommendedTotalLayers)
{
    public bool HasQuantity => OrderQuantity > 0;

    public string RecommendedPurchase =>
        string.Join(
            " + ",
            new[]
            {
                RecommendedPallets > 0
                    ? $"{RecommendedPallets} {(RecommendedPallets == 1 ? "pallet" : "pallets")}"
                    : null,
                RecommendedLooseLayers > 0
                    ? $"{RecommendedLooseLayers} {(RecommendedLooseLayers == 1 ? "layer" : "layers")}"
                    : null
            }.Where(value => value is not null));
}

public static class PaverWallCalculator
{
    public static ProjectProductConfiguration? Resolve(
        Product product,
        ProductVariant? variant = null)
    {
        var searchText =
            $"{product.Name} {product.ShopifyTitle} {product.ShopifyProductType} {product.ShopifyTags}"
                .ToLowerInvariant();
        var kind = product.ProjectCalculatorType?.Trim().ToLowerInvariant() switch
        {
            "paver" => ProjectCalculatorKind.Paver,
            "wall" => ProjectCalculatorKind.Wall,
            _ when searchText.Contains("wall", StringComparison.Ordinal) ||
                   searchText.Contains("tribute", StringComparison.Ordinal) =>
                ProjectCalculatorKind.Wall,
            _ when searchText.Contains("paver", StringComparison.Ordinal) ||
                   searchText.Contains("discover", StringComparison.Ordinal) =>
                ProjectCalculatorKind.Paver,
            _ => (ProjectCalculatorKind?)null
        };
        if (kind is null)
        {
            return null;
        }

        var coverage =
            variant?.CoveragePerOrderUnitSqFt ??
            product.CoveragePerOrderUnitSqFt ??
            0m;
        var orderLabel =
            variant?.CalculatorOrderUnitLabel ??
            product.CalculatorOrderUnitLabel ??
            "order unit";
        var pieces =
            variant?.PiecesPerOrderUnit ??
            product.PiecesPerOrderUnit;
        var length =
            variant?.CalculatorUnitLengthInches ??
            product.CalculatorUnitLengthInches;
        var height =
            variant?.CalculatorUnitHeightInches ??
            product.CalculatorUnitHeightInches;
        var layers =
            variant?.LayersPerPallet ??
            product.LayersPerPallet;
        var layerCoverage =
            variant?.SquareFeetPerLayer ??
            product.SquareFeetPerLayer;
        var palletWeight =
            variant?.PalletWeightLbs ??
            product.PalletWeightLbs;

        if (kind == ProjectCalculatorKind.Wall &&
            searchText.Contains("tribute", StringComparison.Ordinal))
        {
            length ??= 16m;
            height ??= 6m;
        }

        var standardDiscover =
            kind == ProjectCalculatorKind.Paver &&
            searchText.Contains("discover paver", StringComparison.Ordinal) &&
            !searchText.Contains("grand", StringComparison.Ordinal);
        if (standardDiscover)
        {
            var variantTitle = variant?.Title.ToLowerInvariant() ?? string.Empty;
            if (variantTitle.Contains("layer", StringComparison.Ordinal) &&
                !variantTitle.Contains("pallet", StringComparison.Ordinal))
            {
                coverage = 12.45m;
                orderLabel = "layer";
                layers = 1;
                layerCoverage = 12.45m;
                palletWeight = null;
            }
            else if (variantTitle.Contains("pallet", StringComparison.Ordinal))
            {
                coverage = 99.60m;
                orderLabel = "pallet";
                layers = 8;
                layerCoverage = 12.45m;
                palletWeight = 3175;
            }
            else
            {
                coverage = coverage > 0 ? coverage : 99.60m;
                orderLabel =
                    string.Equals(
                        orderLabel,
                        "order unit",
                        StringComparison.OrdinalIgnoreCase)
                            ? "pallet"
                            : orderLabel;
                layers ??= 8;
                layerCoverage ??= 12.45m;
                palletWeight ??= 3175;
            }
        }

        return new(
            kind.Value,
            Math.Max(0m, coverage),
            orderLabel.Trim(),
            pieces,
            length,
            height,
            layers,
            layerCoverage,
            palletWeight,
            standardDiscover);
    }

    public static PaverWallCalculation Calculate(
        ProjectProductConfiguration configuration,
        ProjectShape shape,
        decimal lengthFeet,
        decimal widthFeet,
        decimal diameterFeet,
        decimal wallHeightFeet,
        decimal openingsSqFt,
        decimal extraPercent)
    {
        var baseArea = configuration.Kind switch
        {
            ProjectCalculatorKind.Paver
                when shape == ProjectShape.Circle && diameterFeet > 0 =>
                (decimal)Math.PI * (diameterFeet / 2m) * (diameterFeet / 2m),
            ProjectCalculatorKind.Paver
                when lengthFeet > 0 && widthFeet > 0 =>
                lengthFeet * widthFeet,
            ProjectCalculatorKind.Wall
                when lengthFeet > 0 && wallHeightFeet > 0 =>
                Math.Max(
                    lengthFeet * wallHeightFeet - Math.Max(0m, openingsSqFt),
                    0m),
            _ => 0m
        };
        var requiredArea =
            baseArea * (1m + Math.Max(0m, extraPercent) / 100m);
        var orderQuantity =
            configuration.CoveragePerOrderUnitSqFt > 0 && requiredArea > 0
                ? Math.Ceiling(
                    requiredArea /
                    configuration.CoveragePerOrderUnitSqFt)
                : 0m;
        var blockFaceArea =
            configuration.UnitLengthInches.GetValueOrDefault() > 0 &&
            configuration.UnitHeightInches.GetValueOrDefault() > 0
                ? configuration.UnitLengthInches!.Value *
                  configuration.UnitHeightInches!.Value / 144m
                : 0m;
        var blocks =
            configuration.Kind == ProjectCalculatorKind.Wall &&
            blockFaceArea > 0 &&
            requiredArea > 0
                ? (int)Math.Ceiling(requiredArea / blockFaceArea)
                : 0;
        var courses =
            configuration.Kind == ProjectCalculatorKind.Wall &&
            configuration.UnitHeightInches.GetValueOrDefault() > 0 &&
            wallHeightFeet > 0
                ? (int)Math.Ceiling(
                    wallHeightFeet * 12m /
                    configuration.UnitHeightInches!.Value)
                : 0;

        var totalLayers =
            configuration.IsStandardDiscover && requiredArea > 0
                ? (int)Math.Ceiling(requiredArea / 12.45m)
                : 0;
        var pallets = totalLayers / 8;
        var looseLayers = totalLayers % 8;
        var providedCoverage = configuration.IsStandardDiscover
            ? totalLayers * 12.45m
            : orderQuantity * configuration.CoveragePerOrderUnitSqFt;

        return new(
            Math.Round(baseArea, 2),
            Math.Round(requiredArea, 2),
            orderQuantity,
            Pluralize(configuration.OrderUnitLabel, orderQuantity),
            Math.Round(providedCoverage, 2),
            blocks,
            courses,
            pallets,
            looseLayers,
            totalLayers);
    }

    private static string Pluralize(string label, decimal quantity)
    {
        var normalized = string.IsNullOrWhiteSpace(label)
            ? "order unit"
            : label.Trim();
        if (quantity == 1 || normalized.EndsWith('s'))
        {
            return normalized;
        }

        return normalized.EndsWith('y')
            ? $"{normalized[..^1]}ies"
            : $"{normalized}s";
    }
}
