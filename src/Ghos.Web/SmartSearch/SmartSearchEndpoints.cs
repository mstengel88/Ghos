using Microsoft.AspNetCore.Mvc;

namespace Ghos.Web.SmartSearch;

public static class SmartSearchEndpoints
{
    public static IEndpointRouteBuilder MapSmartSearchEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/storefront/search",
                async (
                    [FromQuery] string? q,
                    [FromQuery] int? limit,
                    SmartProductSearchService searchService,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var origin =
                        httpContext.Request.Headers.Origin.ToString();
                    if (origin is
                        "https://greenhillssupply.com" or
                        "https://www.greenhillssupply.com")
                    {
                        httpContext.Response.Headers.AccessControlAllowOrigin =
                            origin;
                        httpContext.Response.Headers.Vary = "Origin";
                    }

                    var response = await searchService.SearchAsync(
                        q,
                        limit is null or 0 ? 8 : limit.Value,
                        "Storefront API",
                        cancellationToken);
                    return Results.Ok(response);
                })
            .AllowAnonymous()
            .RequireRateLimiting("storefront-search");

        endpoints.MapPost(
                "/api/storefront/search/{searchEventId:guid}/selection",
                async (
                    Guid searchEventId,
                    SmartSearchSelectionRequest request,
                    SmartProductSearchService searchService,
                    CancellationToken cancellationToken) =>
                    await searchService.RecordSelectionAsync(
                        searchEventId,
                        request.ProductId,
                        cancellationToken)
                        ? Results.NoContent()
                        : Results.NotFound())
            .AllowAnonymous()
            .RequireRateLimiting("storefront-search");

        return endpoints;
    }
}

public sealed record SmartSearchSelectionRequest(Guid ProductId);
