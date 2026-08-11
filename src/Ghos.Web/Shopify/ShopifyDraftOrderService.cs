using System.Text.RegularExpressions;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.Shopify;

public sealed record ShopifyQuoteDraftResult(
    string Id,
    string Name,
    string AdminUrl,
    bool UpdatedExisting);

public sealed class ShopifyDraftOrderService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ShopifyDraftOrderClient shopifyClient)
{
    public async Task<ShopifyQuoteDraftResult> CreateFromQuoteAsync(
        Guid quoteId,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var quote = await dbContext.CustomerQuotes
            .Include(item => item.Lines.OrderBy(line => line.SortOrder))
                .ThenInclude(line => line.ProductVariant)
            .SingleOrDefaultAsync(
                item => item.Id == quoteId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The GHOS quote could not be found.");

        if (quote.Lines.Count == 0 ||
            quote.Lines.All(line => line.Quantity <= 0))
        {
            throw new InvalidOperationException(
                "The quote needs at least one product before it can be sent to Shopify.");
        }

        var input = BuildInput(quote);
        await AddCalculatedShippingRateAsync(
            quote,
            input,
            cancellationToken);
        var updatingExisting =
            !string.IsNullOrWhiteSpace(quote.ShopifyDraftOrderId);
        var saved = updatingExisting
            ? await shopifyClient.UpdateAsync(
                quote.ShopifyDraftOrderId!,
                input,
                cancellationToken)
            : await shopifyClient.CreateAsync(
                input,
                cancellationToken);

        quote.ShopifyDraftOrderId = saved.Id;
        quote.ShopifyDraftOrderUrl = saved.AdminUrl;
        quote.Status = QuoteStatus.ReadyForReview;
        quote.UpdatedAtUtc = DateTime.UtcNow;
        quote.UpdatedByUserId = userId;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ShopifyQuoteDraftResult(
            saved.Id,
            saved.Name,
            saved.AdminUrl,
            updatingExisting);
    }

    internal static Dictionary<string, object?> BuildInput(
        CustomerQuote quote)
    {
        var customAttributes = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["key"] = "GHOS Quote",
                ["value"] = quote.QuoteNumber
            },
            new()
            {
                ["key"] = "GHOS Audience",
                ["value"] = quote.Audience.ToString()
            }
        };
        var deliveryAmount = ResolveDeliveryAmount(quote);
        if (deliveryAmount > 0)
        {
            customAttributes.Add(new Dictionary<string, object?>
            {
                ["key"] = "Quoted Delivery Estimate",
                ["value"] = deliveryAmount.ToString("C2")
            });
            customAttributes.Add(new Dictionary<string, object?>
            {
                ["key"] = "Shipping",
                ["value"] = "Calculate with Shopify shipping rates"
            });
        }

        var input = new Dictionary<string, object?>
        {
            ["lineItems"] = quote.Lines
                .Where(line => line.Quantity > 0)
                .OrderBy(line => line.SortOrder)
                .Select(BuildLineItem)
                .ToList(),
            ["taxExempt"] = quote.IsTaxExempt,
            ["tags"] = new[]
            {
                "GHOS",
                "GHOS Quote",
                quote.QuoteNumber
            },
            ["customAttributes"] = customAttributes,
            ["note"] = BuildNote(quote)
        };

        AddIfPresent(input, "email", quote.Email);
        AddIfPresent(input, "phone", quote.Phone);

        if (HasShopifyPurchasingCompany(quote))
        {
            input["purchasingEntity"] = new Dictionary<string, object?>
            {
                ["purchasingCompany"] =
                    new Dictionary<string, object?>
                    {
                        ["companyId"] = quote.ShopifyCompanyId,
                        ["companyContactId"] =
                            quote.ShopifyCompanyContactId,
                        ["companyLocationId"] =
                            quote.ShopifyCompanyLocationId
                    }
            };
        }

        var shippingAddress = BuildAddress(
            quote.CustomerName,
            quote.CompanyName,
            quote.AddressLine1,
            quote.AddressLine2,
            quote.City,
            quote.State,
            quote.PostalCode,
            "United States",
            quote.Phone);
        if (shippingAddress.Count > 0)
        {
            input["shippingAddress"] = shippingAddress;
        }

        var billingAddress = BuildAddress(
            quote.CustomerName,
            quote.CompanyName,
            quote.BillingAddressLine1,
            quote.BillingAddressLine2,
            quote.BillingCity,
            quote.BillingState,
            quote.BillingPostalCode,
            NormalizeCountry(quote.BillingCountry),
            quote.Phone);
        if (billingAddress.Count > 0)
        {
            input["billingAddress"] = billingAddress;
        }

        return input;
    }

    internal static ShopifyDraftOrderShippingRate? SelectShippingRate(
        IEnumerable<ShopifyDraftOrderShippingRate> rates,
        decimal quotedDeliveryAmount)
    {
        var validRates = rates
            .Where(rate =>
                !string.IsNullOrWhiteSpace(rate.Handle) &&
                !Regex.IsMatch(
                    rate.Title,
                    @"\b(pick\s*up|pickup|in[-\s]?store|store pickup|local pickup)\b",
                    RegexOptions.IgnoreCase))
            .ToList();
        if (validRates.Count == 0)
        {
            return null;
        }

        var deliveryRates = validRates
            .Where(rate => Regex.IsMatch(
                rate.Title,
                @"\b(delivery|shipping|ship)\b",
                RegexOptions.IgnoreCase))
            .ToList();
        var candidates = deliveryRates.Count > 0
            ? deliveryRates
            : validRates;

        return candidates
            .OrderBy(rate => rate.Price.HasValue ? 0 : 1)
            .ThenBy(rate => rate.Price.HasValue
                ? Math.Abs(rate.Price.Value - quotedDeliveryAmount)
                : decimal.MaxValue)
            .ThenBy(rate => rate.Title, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private async Task AddCalculatedShippingRateAsync(
        CustomerQuote quote,
        Dictionary<string, object?> input,
        CancellationToken cancellationToken)
    {
        var quotedDeliveryAmount = ResolveDeliveryAmount(quote);
        if (quotedDeliveryAmount <= 0)
        {
            return;
        }

        var rates = await shopifyClient.CalculateShippingRatesAsync(
            input,
            cancellationToken);
        var selectedRate = SelectShippingRate(
            rates,
            quotedDeliveryAmount)
            ?? throw new ShopifyConnectionException(
                "Shopify did not return a delivery rate for this quote. Confirm the delivery address and that the Local-Delivery carrier service is active.");

        input["shippingLine"] = new Dictionary<string, object?>
        {
            ["shippingRateHandle"] = selectedRate.Handle,
            ["title"] = selectedRate.Title
        };

        if (input["customAttributes"] is
            List<Dictionary<string, object?>> customAttributes)
        {
            customAttributes.Add(new Dictionary<string, object?>
            {
                ["key"] = "Shopify Shipping Rate",
                ["value"] = selectedRate.Price.HasValue
                    ? $"{selectedRate.Title} - {selectedRate.Price.Value:C2}"
                    : selectedRate.Title
            });
        }
    }

    internal static bool HasShopifyPurchasingCompany(
        CustomerQuote quote)
    {
        return
            !string.IsNullOrWhiteSpace(quote.ShopifyCompanyId) &&
            !string.IsNullOrWhiteSpace(
                quote.ShopifyCompanyContactId) &&
            !string.IsNullOrWhiteSpace(
                quote.ShopifyCompanyLocationId);
    }

    internal static Dictionary<string, object?> BuildLineItem(
        CustomerQuoteLine line)
    {
        var quantityIsWhole =
            decimal.Truncate(line.Quantity) == line.Quantity &&
            line.Quantity > 0 &&
            line.Quantity <= int.MaxValue;
        var canUseShopifyVariant =
            quantityIsWhole &&
            !string.IsNullOrWhiteSpace(
                line.ShopifyVariantIdSnapshot);

        if (canUseShopifyVariant)
        {
            return new Dictionary<string, object?>
            {
                ["variantId"] = line.ShopifyVariantIdSnapshot,
                ["quantity"] = decimal.ToInt32(line.Quantity),
                ["priceOverride"] = Usd(line.UnitPrice),
                ["customAttributes"] = BuildLineAttributes(line)
            };
        }

        var isFractional = !quantityIsWhole;
        var displayedQuantity = isFractional ? 1 : decimal.ToInt32(
            line.Quantity);
        var displayedUnitPrice = isFractional
            ? line.LineTotal
            : line.UnitPrice;
        var title = isFractional
            ? $"{line.Description} — {line.Quantity:0.####} {line.UnitLabel}"
            : line.Description;
        var customAttributes = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["key"] = "GHOS Unit",
                ["value"] = line.UnitLabel
            },
            new()
            {
                ["key"] = "GHOS Pricing",
                ["value"] = line.PricingLabel
            }
        };
        if (isFractional)
        {
            customAttributes.Add(
                new Dictionary<string, object?>
                {
                    ["key"] = "GHOS Quantity",
                    ["value"] = line.Quantity.ToString("0.####")
                });
        }
        if (!string.IsNullOrWhiteSpace(
                line.ShopifyVariantIdSnapshot))
        {
            customAttributes.Add(
                new Dictionary<string, object?>
                {
                    ["key"] = "Shopify Variant",
                    ["value"] = line.ShopifyVariantIdSnapshot
                });
        }

        var item = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["quantity"] = displayedQuantity,
            ["originalUnitPriceWithCurrency"] = Usd(displayedUnitPrice),
            ["requiresShipping"] = true,
            ["taxable"] = true,
            ["customAttributes"] = customAttributes
        };
        AddIfPresent(item, "sku", line.Sku);
        return item;
    }

    private static List<Dictionary<string, object?>>
        BuildLineAttributes(CustomerQuoteLine line)
    {
        return
        [
            new Dictionary<string, object?>
            {
                ["key"] = "GHOS Unit",
                ["value"] = line.UnitLabel
            },
            new Dictionary<string, object?>
            {
                ["key"] = "GHOS Pricing",
                ["value"] = line.PricingLabel
            },
            new Dictionary<string, object?>
            {
                ["key"] = "Quoted Unit Price",
                ["value"] = line.UnitPrice.ToString(
                    "0.00",
                    System.Globalization.CultureInfo.InvariantCulture)
            }
        ];
    }

    internal static decimal ResolveDeliveryAmount(CustomerQuote quote)
    {
        if (quote.DeliveryAmount > 0)
        {
            return quote.DeliveryAmount;
        }

        if (quote.CustomDeliveryAmount > 0)
        {
            return quote.CustomDeliveryAmount.Value;
        }

        return Math.Max(0m, quote.CalculatedDeliveryAmount ?? 0m);
    }

    private static Dictionary<string, object?> Usd(decimal amount) =>
        new()
        {
            ["amount"] = amount.ToString(
                "0.00",
                System.Globalization.CultureInfo.InvariantCulture),
            ["currencyCode"] = "USD"
        };

    private static Dictionary<string, object?> BuildAddress(
        string customerName,
        string? companyName,
        string? address1,
        string? address2,
        string? city,
        string? province,
        string? postalCode,
        string country,
        string? phone)
    {
        var address = new Dictionary<string, object?>();
        var (firstName, lastName) = SplitName(customerName);
        AddIfPresent(address, "firstName", firstName);
        AddIfPresent(address, "lastName", lastName);
        AddIfPresent(address, "company", companyName);
        AddIfPresent(address, "address1", address1);
        AddIfPresent(address, "address2", address2);
        AddIfPresent(address, "city", city);
        AddIfPresent(address, "province", province);
        AddIfPresent(address, "zip", postalCode);
        AddIfPresent(address, "country", country);
        AddIfPresent(address, "phone", phone);
        return address;
    }

    private static (string FirstName, string LastName) SplitName(
        string name)
    {
        var parts = name
            .Split(
                ' ',
                2,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => (string.Empty, string.Empty),
            1 => (parts[0], string.Empty),
            _ => (parts[0], parts[1])
        };
    }

    private static string NormalizeCountry(string? country) =>
        string.IsNullOrWhiteSpace(country) ||
        country.Equals("US", StringComparison.OrdinalIgnoreCase) ||
        country.Equals("USA", StringComparison.OrdinalIgnoreCase)
            ? "United States"
            : country.Trim();

    private static string BuildNote(CustomerQuote quote)
    {
        var details = new List<string>
        {
            $"Created from GHOS quote {quote.QuoteNumber}."
        };
        if (!string.IsNullOrWhiteSpace(quote.CustomerNotes))
        {
            details.Add(quote.CustomerNotes.Trim());
        }
        if (!string.IsNullOrWhiteSpace(quote.DeliverySummary))
        {
            details.Add($"Delivery: {quote.DeliverySummary.Trim()}");
        }
        details.Add(
            quote.IsTaxExempt
                ? "Quoted tax: Tax exempt."
                : $"Quoted tax: {quote.TaxRateLabel ?? "Tax"} " +
                  $"({quote.TaxRate:P3}) = {quote.TaxAmount:C2}.");
        return string.Join(Environment.NewLine + Environment.NewLine, details);
    }

    private static void AddIfPresent(
        IDictionary<string, object?> target,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value.Trim();
        }
    }
}
