using Ghos.Web.Auth;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.Assets;

public static class AssetEndpoints
{
    public static IEndpointRouteBuilder MapAssetEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/asset-files/{assetId:guid}",
                async (
                    Guid assetId,
                    IDbContextFactory<ApplicationDbContext> dbContextFactory,
                    AssetStorageService storageService,
                    CancellationToken cancellationToken) =>
                {
                    await using var dbContext =
                        await dbContextFactory.CreateDbContextAsync(cancellationToken);
                    var asset = await dbContext.DigitalAssets
                        .AsNoTracking()
                        .Where(item => item.Id == assetId)
                        .Select(item => new
                        {
                            item.RelativePath,
                            item.ContentType
                        })
                        .SingleOrDefaultAsync(cancellationToken);

                    if (asset is null)
                    {
                        return Results.NotFound();
                    }

                    string absolutePath;

                    try
                    {
                        absolutePath = storageService.GetAbsolutePath(asset.RelativePath);
                    }
                    catch (AssetStorageException)
                    {
                        return Results.NotFound();
                    }

                    return File.Exists(absolutePath)
                        ? Results.File(
                            absolutePath,
                            asset.ContentType,
                            enableRangeProcessing: true)
                        : Results.NotFound();
                })
            .RequireAuthorization(GhosPolicies.Assets);

        return endpoints;
    }
}
