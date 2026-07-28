using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.Shopify;

public sealed record ShopifyQuoteDraftResult(
    string Id,
    string Name,
    string AdminUrl,
    bool AlreadyCreated);

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

        if (!string.IsNullOrWhiteSpace(quote.ShopifyDraftOrderId) &&
            !string.IsNullOrWhiteSpace(quote.ShopifyDraftOrderUrl))
        {
            return new ShopifyQuoteDraftResult(
                quote.ShopifyDraftOrderId,
                quote.QuoteNumber,
                quote.ShopifyDraftOrderUrl,
                true);
        }

        if (quote.Lines.Count == 0 ||
            quote.Lines.All(line => line.Quantity <= 0))
        {
            throw new InvalidOperationException(
                "The quote needs at least one product before it can be sent to Shopify.");
        }

        var input = BuildInput(quote);
        var created = await shopifyClient.CreateAsync(
            input,
            cancellationToken);

        quote.ShopifyDraftOrderId = created.Id;
        quote.ShopifyDraftOrderUrl = created.AdminUrl;
        quote.Status = QuoteStatus.ReadyForReview;
        quote.UpdatedAtUtc = DateTime.UtcNow;
        quote.UpdatedByUserId = userId;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ShopifyQuoteDraftResult(
            created.Id,
            created.Name,
            created.AdminUrl,
            false);
    }

    private static Dictionary<string, object?> BuildInput(
        CustomerQuote quote)
    {
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
            ["customAttributes"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["key"] = "GHOS Quote",
                    ["value"] = quote.QuoteNumber
                },
                new Dictionary<string, object?>
                {
                    ["key"] = "GHOS Audience",
                    ["value"] = quote.Audience.ToString()
                }
            },
            ["note"] = BuildNote(quote)
        };

        AddIfPresent(input, "email", quote.Email);
        AddIfPresent(input, "phone", quote.Phone);

        if (quote.DeliveryAmount > 0)
        {
            input["shippingLine"] = new Dictionary<string, object?>
            {
                ["title"] =
                    quote.DeliveryServiceName ??
                    quote.DeliveryDescription ??
                    "Green Hills delivery",
                ["price"] = quote.DeliveryAmount
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

    private static Dictionary<string, object?> BuildLineItem(
        CustomerQuoteLine line)
    {
        var quantityIsWhole =
            decimal.Truncate(line.Quantity) == line.Quantity &&
            line.Quantity > 0 &&
            line.Quantity <= int.MaxValue;
        var currentVariantPrice = line.ProductVariant?.Price;
        var canUseShopifyVariant =
            quantityIsWhole &&
            !string.IsNullOrWhiteSpace(
                line.ShopifyVariantIdSnapshot) &&
            currentVariantPrice is not null &&
            Math.Abs(currentVariantPrice.Value - line.UnitPrice) < .01m;

        if (canUseShopifyVariant)
        {
            return new Dictionary<string, object?>
            {
                ["variantId"] = line.ShopifyVariantIdSnapshot,
                ["quantity"] = decimal.ToInt32(line.Quantity)
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
            ["originalUnitPrice"] = displayedUnitPrice,
            ["customAttributes"] = customAttributes
        };
        AddIfPresent(item, "sku", line.Sku);
        return item;
    }

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
