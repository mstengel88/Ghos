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
                    CancellationToken cancellationToken) =>
                {
                    var response = await searchService.SearchAsync(
                        q,
                        limit is null or 0 ? 8 : limit.Value,
                        "Storefront API",
                        cancellationToken);
                    return Results.Ok(response);
                })
            .AllowAnonymous()
            .RequireCors("storefront-search-cors")
            .RequireRateLimiting("storefront-search");

        endpoints.MapGet(
                "/api/storefront/search/suggestions",
                async (
                    [FromQuery] string? q,
                    [FromQuery] int? limit,
                    SmartSearchSuggestionService suggestionService,
                    CancellationToken cancellationToken) =>
                    Results.Ok(
                        await suggestionService.GetSuggestionsAsync(
                            q,
                            limit is null or 0 ? 8 : limit.Value,
                            cancellationToken)))
            .AllowAnonymous()
            .RequireCors("storefront-search-cors")
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
            .RequireCors("storefront-search-cors")
            .RequireRateLimiting("storefront-search");

        return endpoints;
    }
}

public sealed record SmartSearchSelectionRequest(Guid ProductId);
