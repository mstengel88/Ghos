using Ghos.Web.Data;
using Ghos.Web.Shopify;
using Xunit;

namespace Ghos.Web.Tests;

public sealed class ShopifyDraftOrderPayloadTests
{
    [Fact]
    public void VariantLine_KeepsShopifyMerchandiseAndQuotedPrice()
    {
        var item = ShopifyDraftOrderService.BuildLineItem(
            new CustomerQuoteLine
            {
                Description = "#2 Stone",
                ShopifyVariantIdSnapshot =
                    "gid://shopify/ProductVariant/123",
                Quantity = 22m,
                UnitPrice = 22.94m,
                UnitLabel = "PER TON",
                PricingLabel = "Contractor Tier 2"
            });

        Assert.Equal(
            "gid://shopify/ProductVariant/123",
            item["variantId"]);
        Assert.Equal(22, item["quantity"]);
        Assert.Equal(
            "22.94",
            MoneyAmount(item, "priceOverride"));
        Assert.DoesNotContain("title", item.Keys);
        Assert.DoesNotContain("originalUnitPrice", item.Keys);
    }

    [Fact]
    public void CustomLine_IsExplicitlyShippable()
    {
        var item = ShopifyDraftOrderService.BuildLineItem(
            new CustomerQuoteLine
            {
                Description = "Custom material",
                Quantity = 1.5m,
                UnitPrice = 20m,
                LineTotal = 30m,
                UnitLabel = "ton",
                PricingLabel = "Custom"
            });

        Assert.Equal(true, item["requiresShipping"]);
        Assert.Equal(
            "30.00",
            MoneyAmount(item, "originalUnitPriceWithCurrency"));
    }

    [Fact]
    public void DeliveryAmount_FallsBackToCalculatedOrCustomValue()
    {
        Assert.Equal(
            207.93m,
            ShopifyDraftOrderService.ResolveDeliveryAmount(
                new CustomerQuote
                {
                    CalculatedDeliveryAmount = 207.93m
                }));
        Assert.Equal(
            125m,
            ShopifyDraftOrderService.ResolveDeliveryAmount(
                new CustomerQuote
                {
                    CalculatedDeliveryAmount = 207.93m,
                    CustomDeliveryAmount = 125m
                }));
    }

    [Fact]
    public void DraftInput_SendsDeliveryAsShopifyMoney()
    {
        var input = ShopifyDraftOrderService.BuildInput(
            new CustomerQuote
            {
                QuoteNumber = "GH-TEST",
                CustomerName = "Test Customer",
                DeliveryAmount = 207.93m,
                DeliveryServiceName = "Aggregate Delivery",
                Lines =
                [
                    new CustomerQuoteLine
                    {
                        Description = "#2 Stone",
                        ShopifyVariantIdSnapshot =
                            "gid://shopify/ProductVariant/123",
                        Quantity = 22m,
                        UnitPrice = 22.94m,
                        UnitLabel = "PER TON",
                        PricingLabel = "Contractor Tier 2"
                    }
                ]
            });

        var shippingLine = Assert.IsAssignableFrom<
            IReadOnlyDictionary<string, object?>>(
                input["shippingLine"]);
        Assert.Equal("Aggregate Delivery", shippingLine["title"]);
        Assert.Equal(
            "207.93",
            MoneyAmount(shippingLine, "priceWithCurrency"));
    }

    private static object? MoneyAmount(
        IReadOnlyDictionary<string, object?> item,
        string key)
    {
        var money = Assert.IsAssignableFrom<
            IReadOnlyDictionary<string, object?>>(item[key]);
        Assert.Equal("USD", money["currencyCode"]);
        return money["amount"];
    }
}
