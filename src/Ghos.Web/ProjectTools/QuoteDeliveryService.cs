using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.ProjectTools;

public sealed record QuoteDeliveryItem(
    string? Sku,
    decimal Quantity,
    string? PickupVendor);

public sealed record QuoteDeliveryRequest(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string Country,
    decimal? RatePerMinute,
    IReadOnlyList<QuoteDeliveryItem> Items);

public sealed record QuoteDeliveryResult(
    decimal Amount,
    string ServiceName,
    string Description,
    string Eta,
    string Summary,
    string SourceBreakdownJson,
    bool IsOutsideDeliveryArea = false,
    decimal? OutsideDeliveryMiles = null);

public sealed class QuoteDeliveryService(
    HttpClient httpClient,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IConfiguration configuration,
    ILogger<QuoteDeliveryService> logger)
{
    private const decimal DefaultTruckCapacity = 22m;

    public async Task<QuoteDeliveryResult> CalculateAsync(
        QuoteDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await dbContext.QuoteConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken) ?? new QuoteConfiguration();

        if (!settings.EnableCalculatedRates)
        {
            return Unavailable("Calculated delivery rates are currently disabled");
        }

        if (settings.UseTestFlatRate)
        {
            return new(
                settings.TestFlatRate,
                "Test Delivery Rate",
                "Test flat rate enabled",
                "2–4 business days",
                $"Test flat rate: {settings.TestFlatRate:C2}",
                "[]");
        }

        var apiKey = configuration["Quote:GoogleMapsApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unavailable(
                "Google Maps API key is not configured for GHOS quote delivery");
        }

        var customerAddress = string.Join(", ",
            new[]
            {
                request.AddressLine1,
                request.AddressLine2,
                request.City,
                request.State,
                request.PostalCode,
                request.Country
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(customerAddress))
        {
            return Unavailable("Missing destination address");
        }

        var rules = await dbContext.QuoteMaterialRules
            .AsNoTracking()
            .Where(rule => rule.IsActive)
            .OrderBy(rule => rule.SortOrder)
            .ToListAsync(cancellationToken);
        if (rules.Count == 0)
        {
            rules = DefaultRules();
        }

        var origins = await dbContext.QuoteOriginAddresses
            .AsNoTracking()
            .Where(origin => origin.IsActive)
            .ToListAsync(cancellationToken);
        var defaultOrigin = origins.FirstOrDefault(origin => origin.IsDefault) ??
            new QuoteOriginAddress
            {
                Label = settings.DefaultOriginLabel,
                Address = settings.DefaultOriginAddress,
                IsDefault = true
            };
        var groups = request.Items
            .Where(item => item.Quantity > 0)
            .Select(item => CreateGroup(item, rules, origins, defaultOrigin))
            .GroupBy(group => new
            {
                group.OriginLabel,
                group.OriginAddress,
                group.MaterialName,
                group.LoadKey,
                group.TruckCapacity
            })
            .Select(group => new DeliveryGroup(
                group.Key.OriginLabel,
                group.Key.OriginAddress,
                group.Key.MaterialName,
                group.Key.LoadKey,
                group.Key.TruckCapacity,
                group.Sum(item => item.Quantity)))
            .ToList();
        if (groups.Count == 0)
        {
            groups.Add(new DeliveryGroup(
                defaultOrigin.Label,
                defaultOrigin.Address,
                "Material",
                "material",
                DefaultTruckCapacity,
                1m));
        }

        var pickupAddresses = groups
            .Select(group => group.OriginAddress)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matrixOrigins = new List<string> { defaultOrigin.Address };
        matrixOrigins.AddRange(pickupAddresses);
        matrixOrigins.Add(customerAddress);
        var matrix = await GetMatrixAsync(
            matrixOrigins,
            [defaultOrigin.Address, customerAddress],
            apiKey,
            cancellationToken);
        if (matrix is null)
        {
            return Unavailable("Unable to calculate delivery route");
        }

        var customerOriginIndex = matrixOrigins.Count - 1;
        var pickupIndexByAddress = pickupAddresses
            .Select((address, index) => (address, index: index + 1))
            .ToDictionary(item => item.address, item => item.index,
                StringComparer.OrdinalIgnoreCase);
        var ratePerMinute = request.RatePerMinute.GetValueOrDefault() > 0
            ? request.RatePerMinute!.Value
            : settings.DefaultRatePerMinute;
        var totalAmount = 0m;
        var totalLoads = 0;
        var maxOneWayMiles = 0m;
        var sourceBreakdown = new List<object>();

        foreach (var group in groups)
        {
            var pickupIndex = pickupIndexByAddress[group.OriginAddress];
            var pickupToYard = matrix[pickupIndex][0];
            var pickupToCustomer = matrix[pickupIndex][1];
            var customerToYard = matrix[customerOriginIndex][0];
            if (pickupToYard is null ||
                pickupToCustomer is null ||
                customerToYard is null)
            {
                continue;
            }

            var loads = Math.Max(
                1,
                (int)Math.Ceiling(group.Quantity / group.TruckCapacity));
            var loopMinutes =
                pickupToYard.Minutes +
                pickupToCustomer.Minutes +
                customerToYard.Minutes;
            var loopMiles =
                pickupToYard.Miles +
                pickupToCustomer.Miles +
                customerToYard.Miles;
            var groupAmount = loopMinutes * ratePerMinute * loads;
            if (settings.EnableRemoteSurcharge &&
                request.PostalCode.StartsWith('9'))
            {
                groupAmount += 3m;
            }

            totalAmount += Math.Round(groupAmount, 2);
            totalLoads += loads;
            maxOneWayMiles = Math.Max(
                maxOneWayMiles,
                pickupToCustomer.Miles);
            sourceBreakdown.Add(new
            {
                source = group.OriginLabel,
                material = group.MaterialName,
                quantity = group.Quantity,
                truckCapacity = group.TruckCapacity,
                loads,
                loopMinutes = Math.Round(loopMinutes),
                loopMiles = Math.Round(loopMiles, 1),
                amount = Math.Round(groupAmount, 2)
            });
        }

        if (maxOneWayMiles > settings.MaximumDeliveryRadiusMiles)
        {
            return new(
                .01m,
                "Call for delivery quote",
                $"Outside delivery area — call {settings.OutsideRadiusPhone}",
                "Same business day",
                "Custom delivery quote required",
                System.Text.Json.JsonSerializer.Serialize(sourceBreakdown),
                true,
                maxOneWayMiles);
        }

        var materialNames = groups
            .Select(group => group.MaterialName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var serviceName = materialNames.Count switch
        {
            1 => $"{materialNames[0]} Delivery",
            > 1 => "Bulk Material Delivery",
            _ => "Green Hills Delivery"
        };
        if (totalLoads > 1)
        {
            serviceName += $" ({totalLoads} Loads)";
        }

        var sourceLabels = groups
            .Select(group => group.OriginLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var description = totalLoads > 1
            ? $"{totalLoads} truck loads required for this order"
            : "Standard delivery pricing";
        if (settings.ShowVendorSource && sourceLabels.Count > 0)
        {
            description += $" Source: {string.Join(", ", sourceLabels)}.";
        }

        return new(
            Math.Round(totalAmount, 2),
            serviceName,
            description,
            "2–4 business days",
            $"Shipping: {totalAmount:C2}",
            System.Text.Json.JsonSerializer.Serialize(sourceBreakdown));
    }

    private static DeliveryGroup CreateGroup(
        QuoteDeliveryItem item,
        IReadOnlyList<QuoteMaterialRule> rules,
        IReadOnlyList<QuoteOriginAddress> origins,
        QuoteOriginAddress defaultOrigin)
    {
        var sku = item.Sku?.Trim() ?? string.Empty;
        var prefix = sku.Length >= 3 &&
            sku[..3].All(char.IsDigit)
                ? sku[..3]
                : string.Empty;
        var rule = rules.FirstOrDefault(candidate =>
            candidate.SkuPrefix == prefix);
        var vendorLabel = !string.IsNullOrWhiteSpace(item.PickupVendor)
            ? item.PickupVendor
            : rule?.VendorSource;
        var origin = origins.FirstOrDefault(candidate =>
                !string.IsNullOrWhiteSpace(vendorLabel) &&
                candidate.Label.Equals(
                    vendorLabel,
                    StringComparison.OrdinalIgnoreCase)) ??
            origins.FirstOrDefault(candidate =>
                !string.IsNullOrWhiteSpace(vendorLabel) &&
                candidate.Label.Contains(
                    vendorLabel,
                    StringComparison.OrdinalIgnoreCase)) ??
            defaultOrigin;

        return new DeliveryGroup(
            origin.Label,
            origin.Address,
            rule?.MaterialName ?? "Material",
            string.IsNullOrWhiteSpace(sku)
                ? rule?.MaterialName.ToLowerInvariant() ?? "material"
                : sku.ToLowerInvariant(),
            rule?.TruckCapacity > 0
                ? rule.TruckCapacity
                : DefaultTruckCapacity,
            item.Quantity);
    }

    private async Task<List<List<DistancePoint?>>?> GetMatrixAsync(
        IReadOnlyList<string> origins,
        IReadOnlyList<string> destinations,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var url =
            "https://maps.googleapis.com/maps/api/distancematrix/json" +
            $"?origins={Uri.EscapeDataString(string.Join("|", origins))}" +
            $"&destinations={Uri.EscapeDataString(string.Join("|", destinations))}" +
            $"&key={Uri.EscapeDataString(apiKey)}&units=imperial";

        try
        {
            var response = await httpClient.GetFromJsonAsync<DistanceMatrixResponse>(
                url,
                cancellationToken);
            if (response?.Status != "OK")
            {
                logger.LogWarning(
                    "Google Distance Matrix returned {Status}: {Error}",
                    response?.Status,
                    response?.ErrorMessage);
                return null;
            }

            return response.Rows.Select(row =>
                row.Elements.Select(element =>
                    element.Status == "OK" &&
                    element.Duration?.Value is not null &&
                    element.Distance?.Value is not null
                        ? new DistancePoint(
                            (decimal)element.Duration.Value / 60m,
                            Math.Round(
                                (decimal)element.Distance.Value / 1609.34m,
                                1))
                        : null).ToList()).ToList();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Unable to retrieve a quote delivery distance matrix.");
            return null;
        }
    }

    private static QuoteDeliveryResult Unavailable(string message) =>
        new(0m, "Delivery Unavailable", message, "Unavailable", message, "[]");

    private static List<QuoteMaterialRule> DefaultRules() =>
    [
        new() { SkuPrefix = "100", MaterialName = "Aggregate", TruckCapacity = 22m, VendorSource = "Aggregate", SortOrder = 100 },
        new() { SkuPrefix = "300", MaterialName = "Mulch", TruckCapacity = 25m, VendorSource = "Mulch", SortOrder = 300 },
        new() { SkuPrefix = "400", MaterialName = "Soil", TruckCapacity = 25m, VendorSource = "Soil", SortOrder = 400 },
        new() { SkuPrefix = "499", MaterialName = "Field Run", TruckCapacity = 20m, VendorSource = "Field Run", SortOrder = 499 }
    ];

    private sealed record DeliveryGroup(
        string OriginLabel,
        string OriginAddress,
        string MaterialName,
        string LoadKey,
        decimal TruckCapacity,
        decimal Quantity);

    private sealed record DistancePoint(decimal Minutes, decimal Miles);

    private sealed class DistanceMatrixResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; init; }

        [JsonPropertyName("rows")]
        public List<DistanceMatrixRow> Rows { get; init; } = [];
    }

    private sealed class DistanceMatrixRow
    {
        [JsonPropertyName("elements")]
        public List<DistanceMatrixElement> Elements { get; init; } = [];
    }

    private sealed class DistanceMatrixElement
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("duration")]
        public DistanceValue? Duration { get; init; }

        [JsonPropertyName("distance")]
        public DistanceValue? Distance { get; init; }
    }

    private sealed class DistanceValue
    {
        [JsonPropertyName("value")]
        public double? Value { get; init; }
    }
}
