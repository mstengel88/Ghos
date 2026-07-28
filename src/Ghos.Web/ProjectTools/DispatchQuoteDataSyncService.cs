using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ghos.Web.ProjectTools;

public sealed record DispatchQuoteDataSyncResult(
    int Products,
    int Companies,
    int ImportedQuotes,
    int ExistingQuotes,
    DateTime CompletedAtUtc,
    int MaterialRules = 0,
    int OriginAddresses = 0,
    int Settings = 0);

public sealed partial class DispatchQuoteDataSyncService(
    HttpClient httpClient,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IOptions<DispatchQuoteDataOptions> options,
    ILogger<DispatchQuoteDataSyncService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
    private readonly DispatchQuoteDataOptions _options = options.Value;

    public bool IsConfigured =>
        Uri.TryCreate(_options.SupabaseUrl, UriKind.Absolute, out _) &&
        !string.IsNullOrWhiteSpace(_options.ServiceRoleKey);

    public async Task<DispatchQuoteDataSyncResult?> SynchronizeIfStaleAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var configuration = await dbContext.QuoteConfigurations
            .SingleOrDefaultAsync(cancellationToken);
        var refreshWindow = TimeSpan.FromMinutes(
            Math.Clamp(_options.RefreshMinutes, 5, 1440));
        if (configuration?.DispatchDataLastSyncedAtUtc is not null &&
            DateTime.UtcNow - configuration.DispatchDataLastSyncedAtUtc <
            refreshWindow)
        {
            var materialRuleCount = await dbContext.QuoteMaterialRules
                .CountAsync(cancellationToken);
            var originCount = await dbContext.QuoteOriginAddresses
                .CountAsync(cancellationToken);
            return new(
                configuration.DispatchDataLastProductCount,
                configuration.DispatchDataLastCompanyCount,
                0,
                configuration.DispatchDataLastQuoteCount,
                configuration.DispatchDataLastSyncedAtUtc.Value,
                materialRuleCount,
                originCount,
                1);
        }

        return await SynchronizeAsync(cancellationToken);
    }

    public async Task<DispatchQuoteDataSyncResult> SynchronizeAsync(
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();
        var productRows = await GetAsync<ProductSourceRow>(
            "product_source_map?select=*&order=product_title.asc",
            cancellationToken);
        var companyRows = await GetAsync<B2BCompanyRow>(
            "dispatch_b2b_companies?select=*&order=company_name.asc",
            cancellationToken);
        var quoteRows = await GetAsync<LegacyQuoteRow>(
            "custom_delivery_quotes?select=*&order=created_at.asc",
            cancellationToken);
        var materialRuleRows = await GetAsync<MaterialRuleRow>(
            "shipping_material_rules?select=*&order=sort_order.asc",
            cancellationToken);
        var originRows = await GetAsync<OriginAddressRow>(
            "origin_addresses?select=*&order=label.asc",
            cancellationToken);
        var settingsRows = await GetAsync<AppSettingsRow>(
            "shopify_app_settings?select=*&order=updated_at.desc&limit=1",
            cancellationToken);

        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await ApplyProductsAsync(dbContext, productRows, cancellationToken);
        await ApplyCompaniesAsync(dbContext, companyRows, cancellationToken);
        await ApplyMaterialRulesAsync(
            dbContext,
            materialRuleRows,
            cancellationToken);
        await ApplyOriginsAsync(dbContext, originRows, cancellationToken);
        var (imported, existing) = await ApplyQuotesAsync(
            dbContext,
            quoteRows,
            cancellationToken);

        var configuration = await dbContext.QuoteConfigurations
            .SingleOrDefaultAsync(cancellationToken);
        if (configuration is null)
        {
            configuration = new QuoteConfiguration();
            dbContext.QuoteConfigurations.Add(configuration);
        }
        ApplySettings(configuration, settingsRows.FirstOrDefault());

        var completedAt = DateTime.UtcNow;
        configuration.DispatchDataLastSyncedAtUtc = completedAt;
        configuration.DispatchDataLastProductCount = productRows.Count;
        configuration.DispatchDataLastCompanyCount = companyRows.Count;
        configuration.DispatchDataLastQuoteCount = quoteRows.Count;
        configuration.UpdatedAtUtc = completedAt;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new(
            productRows.Count,
            companyRows.Count,
            imported,
            existing,
            completedAt,
            materialRuleRows.Count,
            originRows.Count,
            settingsRows.Count);
    }

    private async Task<IReadOnlyList<T>> GetAsync<T>(
        string relativePath,
        CancellationToken cancellationToken)
    {
        const int pageSize = 1000;
        var rows = new List<T>();
        for (var start = 0; ; start += pageSize)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_options.SupabaseUrl.TrimEnd('/')}/rest/v1/{relativePath}");
            request.Headers.TryAddWithoutValidation(
                "apikey",
                _options.ServiceRoleKey);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                _options.ServiceRoleKey);
            request.Headers.TryAddWithoutValidation(
                "Range",
                $"{start}-{start + pageSize - 1}");

            using var response = await httpClient.SendAsync(
                request,
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(
                cancellationToken);
            if (response.StatusCode ==
                System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                break;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Dispatch quote data returned HTTP {Status}.",
                    (int)response.StatusCode);
                throw new InvalidOperationException(
                    $"Dispatch quote data returned HTTP {(int)response.StatusCode}.");
            }

            var page = JsonSerializer.Deserialize<List<T>>(
                body,
                JsonOptions) ?? [];
            rows.AddRange(page);
            if (page.Count < pageSize)
            {
                break;
            }
        }

        return rows;
    }

    private static async Task ApplyProductsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<ProductSourceRow> rows,
        CancellationToken cancellationToken)
    {
        var variants = await dbContext.ProductVariants
            .Include(variant => variant.Product)
            .ToListAsync(cancellationToken);
        var byVariantId = variants
            .Where(variant => !string.IsNullOrWhiteSpace(
                variant.ShopifyVariantId))
            .ToDictionary(
                variant => variant.ShopifyVariantId,
                StringComparer.Ordinal);
        var bySku = variants
            .Where(variant => !string.IsNullOrWhiteSpace(variant.Sku))
            .GroupBy(variant => variant.Sku!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var variant = !string.IsNullOrWhiteSpace(row.VariantId) &&
                byVariantId.TryGetValue(row.VariantId, out var variantMatch)
                    ? variantMatch
                    : !string.IsNullOrWhiteSpace(row.Sku) &&
                      bySku.TryGetValue(row.Sku, out var skuMatch)
                        ? skuMatch
                        : null;
            if (variant is null)
            {
                continue;
            }

            variant.ContractorTier1Price =
                row.ContractorTier1Price ?? row.Tier1Price;
            variant.ContractorTier2Price =
                row.ContractorTier2Price ?? row.Tier2Price;
            variant.UnitLabel =
                Clean(row.UnitLabel) ??
                Clean(row.PriceUnitLabel) ??
                variant.UnitLabel;
            variant.ImageUrl = Clean(row.ImageUrl) ?? variant.ImageUrl;
            variant.PickupVendor =
                Clean(row.PickupVendor) ?? variant.PickupVendor;
            if (row.Price is not null)
            {
                variant.Price = Math.Max(0m, row.Price.Value);
            }

            variant.CoveragePerOrderUnitSqFt =
                row.CoveragePerOrderUnitSqFt ??
                variant.CoveragePerOrderUnitSqFt;
            variant.CalculatorOrderUnitLabel =
                Clean(row.CalculatorOrderUnitLabel) ??
                variant.CalculatorOrderUnitLabel;
            variant.PiecesPerOrderUnit =
                row.PiecesPerOrderUnit ?? variant.PiecesPerOrderUnit;
            variant.CalculatorUnitLengthInches =
                row.UnitLengthInches ??
                variant.CalculatorUnitLengthInches;
            variant.CalculatorUnitHeightInches =
                row.UnitHeightInches ??
                variant.CalculatorUnitHeightInches;
            variant.LayersPerPallet =
                row.LayersPerPallet ?? variant.LayersPerPallet;
            variant.SquareFeetPerLayer =
                row.SquareFeetPerLayer ?? variant.SquareFeetPerLayer;
            variant.PalletWeightLbs =
                row.PalletWeightLbs ?? variant.PalletWeightLbs;

            var product = variant.Product;
            product.ProjectCalculatorType =
                Clean(row.ProjectCalculatorType)?.ToLowerInvariant() ??
                product.ProjectCalculatorType;
            product.CoveragePerOrderUnitSqFt =
                row.CoveragePerOrderUnitSqFt ??
                product.CoveragePerOrderUnitSqFt;
            product.CalculatorOrderUnitLabel =
                Clean(row.CalculatorOrderUnitLabel) ??
                product.CalculatorOrderUnitLabel;
            product.PiecesPerOrderUnit =
                row.PiecesPerOrderUnit ?? product.PiecesPerOrderUnit;
            product.CalculatorUnitLengthInches =
                row.UnitLengthInches ??
                product.CalculatorUnitLengthInches;
            product.CalculatorUnitHeightInches =
                row.UnitHeightInches ??
                product.CalculatorUnitHeightInches;
            product.LayersPerPallet =
                row.LayersPerPallet ?? product.LayersPerPallet;
            product.SquareFeetPerLayer =
                row.SquareFeetPerLayer ?? product.SquareFeetPerLayer;
            product.PalletWeightLbs =
                row.PalletWeightLbs ?? product.PalletWeightLbs;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task ApplyMaterialRulesAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<MaterialRuleRow> rows,
        CancellationToken cancellationToken)
    {
        var rules = await dbContext.QuoteMaterialRules
            .ToListAsync(cancellationToken);
        var byPrefix = rules.ToDictionary(
            rule => rule.SkuPrefix,
            StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Where(row =>
            !string.IsNullOrWhiteSpace(row.Prefix) &&
            !string.IsNullOrWhiteSpace(row.MaterialName)))
        {
            var prefix = row.Prefix.Trim();
            if (!byPrefix.TryGetValue(prefix, out var rule))
            {
                rule = new QuoteMaterialRule
                {
                    SkuPrefix = prefix
                };
                dbContext.QuoteMaterialRules.Add(rule);
                byPrefix[prefix] = rule;
            }

            rule.MaterialName = row.MaterialName.Trim();
            rule.TruckCapacity = row.TruckCapacity > 0
                ? row.TruckCapacity
                : 22m;
            rule.VendorSource = Clean(row.VendorSource);
            rule.IsActive = row.IsActive;
            rule.SortOrder = row.SortOrder;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task ApplyOriginsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<OriginAddressRow> rows,
        CancellationToken cancellationToken)
    {
        var origins = await dbContext.QuoteOriginAddresses
            .ToListAsync(cancellationToken);
        var byLabel = origins.ToDictionary(
            origin => origin.Label,
            StringComparer.OrdinalIgnoreCase);
        var defaultRow = rows.FirstOrDefault(row =>
                row.IsActive && row.IsDefault) ??
            rows.FirstOrDefault(row => row.IsActive);

        foreach (var origin in origins)
        {
            origin.IsDefault = false;
        }

        foreach (var row in rows.Where(row =>
            !string.IsNullOrWhiteSpace(row.Label) &&
            !string.IsNullOrWhiteSpace(row.Address)))
        {
            var label = row.Label.Trim();
            if (!byLabel.TryGetValue(label, out var origin))
            {
                origin = new QuoteOriginAddress
                {
                    Label = label
                };
                dbContext.QuoteOriginAddresses.Add(origin);
                byLabel[label] = origin;
            }

            origin.Address = row.Address.Trim();
            origin.IsActive = row.IsActive;
            origin.IsDefault = ReferenceEquals(row, defaultRow);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ApplySettings(
        QuoteConfiguration configuration,
        AppSettingsRow? row)
    {
        if (row is null)
        {
            return;
        }

        configuration.UseTestFlatRate = row.UseTestFlatRate;
        configuration.TestFlatRate =
            Math.Max(0m, row.TestFlatRateCents / 100m);
        configuration.EnableCalculatedRates = row.EnableCalculatedRates;
        configuration.EnableRemoteSurcharge = row.EnableRemoteSurcharge;
        configuration.ShowVendorSource = row.ShowVendorSource;
    }

    private static async Task ApplyCompaniesAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<B2BCompanyRow> rows,
        CancellationToken cancellationToken)
    {
        var companies = await dbContext.QuoteB2BCompanies
            .ToListAsync(cancellationToken);
        var byExternalId = companies.ToDictionary(
            company => company.ExternalId,
            StringComparer.Ordinal);

        foreach (var row in rows.Where(row =>
            !string.IsNullOrWhiteSpace(row.Id) &&
            !string.IsNullOrWhiteSpace(row.CompanyName)))
        {
            if (!byExternalId.TryGetValue(row.Id, out var company))
            {
                company = new QuoteB2BCompany
                {
                    ExternalId = row.Id
                };
                dbContext.QuoteB2BCompanies.Add(company);
                byExternalId[row.Id] = company;
            }

            company.ShopifyCompanyId = row.ShopifyCompanyId ?? string.Empty;
            company.ShopifyCompanyContactId =
                Clean(row.ShopifyCompanyContactId);
            company.ShopifyCompanyLocationId =
                Clean(row.ShopifyLocationId);
            company.CompanyName = row.CompanyName.Trim();
            company.ContractorTier = row.ContractorTier == "tier2"
                ? ContractorTier.Tier2
                : ContractorTier.Tier1;
            company.CatalogTitles = row.CatalogTitles is null
                ? null
                : string.Join(Environment.NewLine, row.CatalogTitles);
            company.ContactName = Clean(row.ContactName);
            company.Email = Clean(row.Email);
            company.Phone = Clean(row.Phone);
            company.BillingAddressLine1 = Clean(row.BillingAddress1);
            company.BillingAddressLine2 = Clean(row.BillingAddress2);
            company.BillingCity = Clean(row.BillingCity);
            company.BillingState = Clean(row.BillingProvince);
            company.BillingPostalCode = Clean(row.BillingPostalCode);
            company.BillingCountry = Clean(row.BillingCountry) ?? "US";
            company.IsTaxExempt = row.TaxExempt;
            company.PaymentTermsName = Clean(row.PaymentTermsName);
            company.PaymentTermsTemplateId =
                Clean(row.PaymentTermsTemplateId);
            company.PaymentTermsDueInDays =
                row.PaymentTermsDueInDays;
            company.UpdatedAtUtc = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<(int Imported, int Existing)> ApplyQuotesAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<LegacyQuoteRow> rows,
        CancellationToken cancellationToken)
    {
        var existingIds = await dbContext.CustomerQuotes
            .Where(quote => quote.LegacyExternalId != null)
            .Select(quote => quote.LegacyExternalId!)
            .ToHashSetAsync(cancellationToken);
        var products = await dbContext.Products
            .Include(product => product.Variants)
            .ToListAsync(cancellationToken);
        var variantsBySku = products
            .SelectMany(product => product.Variants)
            .Where(variant => !string.IsNullOrWhiteSpace(variant.Sku))
            .GroupBy(variant => variant.Sku!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var imported = 0;
        var existing = 0;

        foreach (var row in rows.Where(row =>
            !string.IsNullOrWhiteSpace(row.Id)))
        {
            if (existingIds.Contains(row.Id))
            {
                existing++;
                continue;
            }

            var lineRows = ParseLines(row.LineItems);
            var subtotal = Math.Round(
                lineRows.Sum(line =>
                    Math.Max(0m, line.Quantity) *
                    Math.Max(0m, line.Price)),
                2);
            var total = Math.Max(0m, row.QuoteTotalCents / 100m);
            var delivery = ParseDeliveryAmount(row.ShippingDetails) ??
                EstimateDeliveryAmount(
                    total,
                    subtotal,
                    row.TaxExempt);
            var taxAmount = Math.Max(0m, total - subtotal - delivery);
            var audience = lineRows
                .Select(line => line.Audience?.ToLowerInvariant())
                .FirstOrDefault(value => value is
                    "contractor" or "custom") switch
            {
                "contractor" => QuoteAudience.Contractor,
                "custom" => QuoteAudience.Custom,
                _ => !string.IsNullOrWhiteSpace(row.CompanyName)
                    ? QuoteAudience.Contractor
                    : QuoteAudience.Customer
            };
            var tier = lineRows.Any(line =>
                line.ContractorTier == "tier2")
                    ? ContractorTier.Tier2
                    : ContractorTier.Tier1;
            var createdAt = row.CreatedAt ?? DateTime.UtcNow;
            var quote = new CustomerQuote
            {
                QuoteNumber =
                    $"D2-{createdAt:yyyyMMdd}-{row.Id.Replace("-", string.Empty)[..Math.Min(6, row.Id.Replace("-", string.Empty).Length)].ToUpperInvariant()}",
                Status = QuoteStatus.Draft,
                Audience = audience,
                ContractorTier = tier,
                CustomerName = row.CustomerName?.Trim() ?? string.Empty,
                CompanyName = Clean(row.CompanyName),
                Email = Clean(row.CustomerEmail),
                Phone = Clean(row.CustomerPhone),
                ShopifyCompanyId = Clean(row.ShopifyCompanyId),
                ShopifyCompanyContactId =
                    Clean(row.ShopifyCompanyContactId),
                ShopifyCompanyLocationId =
                    Clean(row.ShopifyCompanyLocationId),
                PaymentTermsName = Clean(row.PaymentTermsName),
                PaymentTermsTemplateId =
                    Clean(row.PaymentTermsTemplateId),
                PaymentTermsDueInDays = row.PaymentTermsDueInDays,
                IsContractor = audience == QuoteAudience.Contractor,
                IsTaxExempt = row.TaxExempt,
                BillingAddressLine1 = Clean(row.BillingAddress1),
                BillingAddressLine2 = Clean(row.BillingAddress2),
                BillingCity = Clean(row.BillingCity),
                BillingState = Clean(row.BillingProvince),
                BillingPostalCode = Clean(row.BillingPostalCode),
                BillingCountry = Clean(row.BillingCountry),
                AddressLine1 = Clean(row.Address1),
                AddressLine2 = Clean(row.Address2),
                City = Clean(row.City),
                State = Clean(row.Province),
                PostalCode = Clean(row.PostalCode),
                Subtotal = subtotal,
                DeliveryAmount = delivery,
                CalculatedDeliveryAmount = delivery,
                TaxRate = row.TaxExempt || subtotal <= 0
                    ? 0m
                    : taxAmount / subtotal,
                TaxRateLabel = row.TaxExempt
                    ? "Tax exempt"
                    : "Imported from Dispatch v2",
                TaxAmount = taxAmount,
                Total = total,
                DeliveryServiceName = Clean(row.ServiceName),
                DeliveryDescription = Clean(row.Description),
                DeliveryEta = Clean(row.Eta),
                DeliverySummary = Clean(row.Summary),
                SourceBreakdownJson =
                    row.SourceBreakdown.ValueKind == JsonValueKind.Undefined
                        ? null
                        : row.SourceBreakdown.GetRawText(),
                CustomerNotes = Clean(row.Description),
                CreatedAtUtc = ToUtc(createdAt),
                UpdatedAtUtc = ToUtc(createdAt),
                CreatedByUserId = Clean(row.CreatedByUserId),
                LegacyExternalId = row.Id
            };
            quote.Lines = lineRows.Select((line, index) =>
            {
                variantsBySku.TryGetValue(
                    line.Sku ?? string.Empty,
                    out var variant);
                return new CustomerQuoteLine
                {
                    ProductId = variant?.ProductId,
                    ProductVariantId = variant?.Id,
                    Description = line.Title?.Trim() ?? line.Sku ?? "Quoted item",
                    Sku = Clean(line.Sku),
                    Vendor = Clean(line.Vendor),
                    ProductHandle = variant?.Product.ShopifyHandle,
                    ImageUrl =
                        variant?.ImageUrl ??
                        variant?.Product.ShopifyFeaturedImageUrl,
                    ShopifyVariantIdSnapshot =
                        Clean(line.VariantId) ??
                        variant?.ShopifyVariantId,
                    UnitLabel =
                        variant?.UnitLabel ??
                        "unit",
                    PricingLabel =
                        Clean(line.PricingLabel) ??
                        QuotePricing.GetPricingLabel(audience, tier),
                    Audience = audience,
                    ContractorTier = audience ==
                        QuoteAudience.Contractor
                            ? tier
                            : null,
                    Quantity = Math.Max(0m, line.Quantity),
                    UnitPrice = Math.Max(0m, line.Price),
                    LineTotal = Math.Round(
                        Math.Max(0m, line.Quantity) *
                        Math.Max(0m, line.Price),
                        2),
                    SortOrder = index
                };
            }).ToList();
            dbContext.CustomerQuotes.Add(quote);
            existingIds.Add(row.Id);
            imported++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (imported, existing);
    }

    private void ValidateConfiguration()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Dispatch quote data synchronization is not configured.");
        }
    }

    private static IReadOnlyList<LegacyQuoteLine> ParseLines(
        JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<LegacyQuoteLine>>(
            element.GetRawText(),
            JsonOptions) ?? [];
    }

    private static decimal EstimateDeliveryAmount(
        decimal total,
        decimal subtotal,
        bool taxExempt)
    {
        var tax = taxExempt ? 0m : Math.Round(subtotal * .055m, 2);
        return Math.Max(0m, total - subtotal - tax);
    }

    private static decimal? ParseDeliveryAmount(string? shippingDetails)
    {
        if (string.IsNullOrWhiteSpace(shippingDetails))
        {
            return null;
        }

        var match = ExactDeliveryRegex().Match(shippingDetails);
        if (!match.Success)
        {
            match = FallbackDeliveryRegex().Match(shippingDetails);
        }

        return match.Success &&
            decimal.TryParse(
                match.Groups[1].Value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var amount)
                    ? amount
                    : null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    [GeneratedRegex(@"=\s*\$?\s*(\d+(?:\.\d{1,2})?)")]
    private static partial Regex ExactDeliveryRegex();

    [GeneratedRegex(
        @"(?:delivery(?: fee| amount)?|shipping)[^\d$]*\$?\s*(\d+(?:\.\d{1,2})?)",
        RegexOptions.IgnoreCase)]
    private static partial Regex FallbackDeliveryRegex();

    private sealed class ProductSourceRow
    {
        [JsonPropertyName("sku")]
        public string? Sku { get; init; }

        [JsonPropertyName("variant_id")]
        public string? VariantId { get; init; }

        [JsonPropertyName("pickup_vendor")]
        public string? PickupVendor { get; init; }

        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; init; }

        [JsonPropertyName("unit_label")]
        public string? UnitLabel { get; init; }

        [JsonPropertyName("price_unit_label")]
        public string? PriceUnitLabel { get; init; }

        [JsonPropertyName("price")]
        public decimal? Price { get; init; }

        [JsonPropertyName("contractor_tier_1_price")]
        public decimal? ContractorTier1Price { get; init; }

        [JsonPropertyName("contractor_tier_2_price")]
        public decimal? ContractorTier2Price { get; init; }

        [JsonPropertyName("tier_1_price")]
        public decimal? Tier1Price { get; init; }

        [JsonPropertyName("tier_2_price")]
        public decimal? Tier2Price { get; init; }

        [JsonPropertyName("project_calculator_type")]
        public string? ProjectCalculatorType { get; init; }

        [JsonPropertyName("coverage_per_order_unit_sq_ft")]
        public decimal? CoveragePerOrderUnitSqFt { get; init; }

        [JsonPropertyName("calculator_order_unit_label")]
        public string? CalculatorOrderUnitLabel { get; init; }

        [JsonPropertyName("pieces_per_order_unit")]
        public int? PiecesPerOrderUnit { get; init; }

        [JsonPropertyName("unit_length_inches")]
        public decimal? UnitLengthInches { get; init; }

        [JsonPropertyName("unit_height_inches")]
        public decimal? UnitHeightInches { get; init; }

        [JsonPropertyName("layers_per_pallet")]
        public int? LayersPerPallet { get; init; }

        [JsonPropertyName("square_feet_per_layer")]
        public decimal? SquareFeetPerLayer { get; init; }

        [JsonPropertyName("pallet_weight_lbs")]
        public int? PalletWeightLbs { get; init; }
    }

    private sealed class MaterialRuleRow
    {
        [JsonPropertyName("prefix")] public string Prefix { get; init; } = string.Empty;
        [JsonPropertyName("material_name")] public string MaterialName { get; init; } = string.Empty;
        [JsonPropertyName("truck_capacity")] public decimal TruckCapacity { get; init; }
        [JsonPropertyName("vendor_source")] public string? VendorSource { get; init; }
        [JsonPropertyName("is_active")] public bool IsActive { get; init; } = true;
        [JsonPropertyName("sort_order")] public int SortOrder { get; init; }
    }

    private sealed class OriginAddressRow
    {
        [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
        [JsonPropertyName("address")] public string Address { get; init; } = string.Empty;
        [JsonPropertyName("is_active")] public bool IsActive { get; init; } = true;
        [JsonPropertyName("is_default")] public bool IsDefault { get; init; }
    }

    private sealed class AppSettingsRow
    {
        [JsonPropertyName("use_test_flat_rate")] public bool UseTestFlatRate { get; init; }
        [JsonPropertyName("test_flat_rate_cents")] public decimal TestFlatRateCents { get; init; } = 5000m;
        [JsonPropertyName("enable_calculated_rates")] public bool EnableCalculatedRates { get; init; } = true;
        [JsonPropertyName("enable_remote_surcharge")] public bool EnableRemoteSurcharge { get; init; } = true;
        [JsonPropertyName("show_vendor_source")] public bool ShowVendorSource { get; init; } = true;
    }

    private sealed class B2BCompanyRow
    {
        [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
        [JsonPropertyName("shopify_company_id")] public string? ShopifyCompanyId { get; init; }
        [JsonPropertyName("shopify_company_contact_id")] public string? ShopifyCompanyContactId { get; init; }
        [JsonPropertyName("shopify_location_id")] public string? ShopifyLocationId { get; init; }
        [JsonPropertyName("company_name")] public string CompanyName { get; init; } = string.Empty;
        [JsonPropertyName("contractor_tier")] public string? ContractorTier { get; init; }
        [JsonPropertyName("catalog_titles")] public string[]? CatalogTitles { get; init; }
        [JsonPropertyName("contact_name")] public string? ContactName { get; init; }
        [JsonPropertyName("email")] public string? Email { get; init; }
        [JsonPropertyName("phone")] public string? Phone { get; init; }
        [JsonPropertyName("billing_address1")] public string? BillingAddress1 { get; init; }
        [JsonPropertyName("billing_address2")] public string? BillingAddress2 { get; init; }
        [JsonPropertyName("billing_city")] public string? BillingCity { get; init; }
        [JsonPropertyName("billing_province")] public string? BillingProvince { get; init; }
        [JsonPropertyName("billing_postal_code")] public string? BillingPostalCode { get; init; }
        [JsonPropertyName("billing_country")] public string? BillingCountry { get; init; }
        [JsonPropertyName("tax_exempt")] public bool TaxExempt { get; init; }
        [JsonPropertyName("payment_terms_name")] public string? PaymentTermsName { get; init; }
        [JsonPropertyName("payment_terms_template_id")] public string? PaymentTermsTemplateId { get; init; }
        [JsonPropertyName("payment_terms_due_in_days")] public int? PaymentTermsDueInDays { get; init; }
    }

    private sealed class LegacyQuoteRow
    {
        [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
        [JsonPropertyName("customer_name")] public string? CustomerName { get; init; }
        [JsonPropertyName("company_name")] public string? CompanyName { get; init; }
        [JsonPropertyName("customer_email")] public string? CustomerEmail { get; init; }
        [JsonPropertyName("customer_phone")] public string? CustomerPhone { get; init; }
        [JsonPropertyName("shopify_company_id")] public string? ShopifyCompanyId { get; init; }
        [JsonPropertyName("shopify_company_contact_id")] public string? ShopifyCompanyContactId { get; init; }
        [JsonPropertyName("shopify_company_location_id")] public string? ShopifyCompanyLocationId { get; init; }
        [JsonPropertyName("payment_terms_name")] public string? PaymentTermsName { get; init; }
        [JsonPropertyName("payment_terms_template_id")] public string? PaymentTermsTemplateId { get; init; }
        [JsonPropertyName("payment_terms_due_in_days")] public int? PaymentTermsDueInDays { get; init; }
        [JsonPropertyName("tax_exempt")] public bool TaxExempt { get; init; }
        [JsonPropertyName("billing_address1")] public string? BillingAddress1 { get; init; }
        [JsonPropertyName("billing_address2")] public string? BillingAddress2 { get; init; }
        [JsonPropertyName("billing_city")] public string? BillingCity { get; init; }
        [JsonPropertyName("billing_province")] public string? BillingProvince { get; init; }
        [JsonPropertyName("billing_postal_code")] public string? BillingPostalCode { get; init; }
        [JsonPropertyName("billing_country")] public string? BillingCountry { get; init; }
        [JsonPropertyName("address1")] public string? Address1 { get; init; }
        [JsonPropertyName("address2")] public string? Address2 { get; init; }
        [JsonPropertyName("city")] public string? City { get; init; }
        [JsonPropertyName("province")] public string? Province { get; init; }
        [JsonPropertyName("postal_code")] public string? PostalCode { get; init; }
        [JsonPropertyName("quote_total_cents")] public decimal QuoteTotalCents { get; init; }
        [JsonPropertyName("service_name")] public string? ServiceName { get; init; }
        [JsonPropertyName("shipping_details")] public string? ShippingDetails { get; init; }
        [JsonPropertyName("description")] public string? Description { get; init; }
        [JsonPropertyName("eta")] public string? Eta { get; init; }
        [JsonPropertyName("summary")] public string? Summary { get; init; }
        [JsonPropertyName("created_by_user_id")] public string? CreatedByUserId { get; init; }
        [JsonPropertyName("source_breakdown")] public JsonElement SourceBreakdown { get; init; }
        [JsonPropertyName("line_items")] public JsonElement LineItems { get; init; }
        [JsonPropertyName("created_at")] public DateTime? CreatedAt { get; init; }
    }

    private sealed class LegacyQuoteLine
    {
        [JsonPropertyName("title")] public string? Title { get; init; }
        [JsonPropertyName("sku")] public string? Sku { get; init; }
        [JsonPropertyName("quantity")] public decimal Quantity { get; init; }
        [JsonPropertyName("vendor")] public string? Vendor { get; init; }
        [JsonPropertyName("price")] public decimal Price { get; init; }
        [JsonPropertyName("variantId")] public string? VariantId { get; init; }
        [JsonPropertyName("pricingLabel")] public string? PricingLabel { get; init; }
        [JsonPropertyName("audience")] public string? Audience { get; init; }
        [JsonPropertyName("contractorTier")] public string? ContractorTier { get; init; }
    }
}
