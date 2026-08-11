using System.Net;
using System.Text;
using System.Text.Json;
using Ghos.Web.ProjectTools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ghos.Web.Tests;

public sealed class QuoteDeliveryServiceTests
{
    [Fact]
    public async Task CalculateAsync_UsesSharedShopifyCalculatorAndPreservesPickupVendor()
    {
        string? requestJson = null;
        var handler = new StubHandler(async request =>
        {
            requestJson = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "cents": 20412,
                      "serviceName": "Sand Delivery",
                      "description": "Standard delivery pricing",
                      "eta": "2–4 business days",
                      "summary": "Shipping: $204.12",
                      "calculator": "shopify-local-delivery"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Quote:ShippingCalculatorUrl"] =
                    "https://calculator.test/api/shipping-estimate",
                ["Shopify:StoreDomain"] = "example.myshopify.com"
            })
            .Build();
        var service = new QuoteDeliveryService(
            new HttpClient(handler),
            configuration,
            NullLogger<QuoteDeliveryService>.Instance);

        var result = await service.CalculateAsync(new QuoteDeliveryRequest(
            "921 Silvernail Rd",
            null,
            "Pewaukee",
            "WI",
            "53072",
            "US",
            2.08m,
            [new QuoteDeliveryItem("150-710", 25m, "Lannon Stone")]));

        Assert.Equal(204.12m, result.Amount);
        Assert.NotNull(requestJson);
        using var payload = JsonDocument.Parse(requestJson);
        Assert.Equal(
            "Lannon Stone",
            payload.RootElement
                .GetProperty("lines")[0]
                .GetProperty("pickupVendor")
                .GetString());
        Assert.Equal(
            "example.myshopify.com",
            payload.RootElement.GetProperty("shop").GetString());
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request);
    }
}
