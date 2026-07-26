using System.IO.Compression;
using System.Text;
using Ghos.Web.Assets;
using Ghos.Web.Auth;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.Marketing;

public static class MarketingPublicationPackageEndpoints
{
    private static readonly TimeZoneInfo CentralTime =
        TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    public static IEndpointRouteBuilder MapMarketingPublicationPackageEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/marketing-content/{contentId:guid}/publication-pack.zip",
                DownloadPublicationPackAsync)
            .RequireAuthorization(GhosPolicies.Marketing);

        return endpoints;
    }

    private static async Task<IResult> DownloadPublicationPackAsync(
        Guid contentId,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        AssetStorageService storageService,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var content = await dbContext.MarketingContentPackages
            .AsNoTracking()
            .Include(item => item.Product)
            .Include(item => item.DigitalAsset)
            .SingleOrDefaultAsync(
                item => item.Id == contentId,
                cancellationToken);

        if (content is null)
        {
            return Results.NotFound();
        }

        var readiness = MarketingReadiness.Evaluate(content);
        if (!readiness.IsReady || content.DigitalAsset is null)
        {
            return Results.BadRequest(
                "This marketing package is not ready for export.");
        }

        string imagePath;
        try
        {
            imagePath = storageService.GetAbsolutePath(
                content.DigitalAsset.RelativePath);
        }
        catch (AssetStorageException)
        {
            return Results.NotFound();
        }

        if (!File.Exists(imagePath))
        {
            return Results.NotFound();
        }

        var imageBytes = await File.ReadAllBytesAsync(
            imagePath,
            cancellationToken);
        var imageData =
            $"data:{content.DigitalAsset.ContentType};base64,{Convert.ToBase64String(imageBytes)}";

        await using var packageStream = new MemoryStream();
        using (var archive = new ZipArchive(
            packageStream,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var template in MarketingTemplateCatalog.All)
            {
                var svg = MarketingCreativeEndpoints.RenderSvg(
                    content,
                    template,
                    imageData);
                var name = Slugify(template.Format);
                await WriteEntryAsync(
                    archive,
                    $"creative/{content.Slug}-{name}.svg",
                    svg,
                    cancellationToken);
            }

            await WriteEntryAsync(
                archive,
                "copy/facebook-caption.txt",
                content.FacebookCaption.Trim(),
                cancellationToken);
            await WriteEntryAsync(
                archive,
                "copy/instagram-caption.txt",
                BuildInstagramCopy(content),
                cancellationToken);
            await WriteEntryAsync(
                archive,
                "video/story-and-reel-plan.txt",
                BuildVideoPlan(content),
                cancellationToken);
            await WriteEntryAsync(
                archive,
                "POSTING-CHECKLIST.txt",
                BuildChecklist(content),
                cancellationToken);
        }

        return Results.File(
            packageStream.ToArray(),
            "application/zip",
            $"{content.Slug}-publication-pack.zip");
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string path,
        string value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: false);
        await writer.WriteAsync(value.AsMemory(), cancellationToken);
    }

    private static string BuildInstagramCopy(
        MarketingContentPackage content) =>
        string.Join(
            Environment.NewLine + Environment.NewLine,
            new[]
            {
                content.InstagramCaption.Trim(),
                content.Hashtags?.Trim()
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string BuildVideoPlan(
        MarketingContentPackage content) =>
        $"""
        STORY PROMPT
        ------------
        {content.StoryPrompt.Trim()}

        REEL SCRIPT AND SHOT LIST
        -------------------------
        {content.ReelScript.Trim()}
        """;

    private static string BuildChecklist(
        MarketingContentPackage content)
    {
        var localTime = content.ScheduledForUtc is null
            ? "Not scheduled"
            : TimeZoneInfo.ConvertTimeFromUtc(
                content.ScheduledForUtc.Value,
                CentralTime).ToString("dddd, MMMM d, yyyy 'at' h:mm tt 'CT'");

        return $"""
        GREEN HILLS SUPPLY — PUBLICATION CHECKLIST
        ==========================================

        Campaign: {content.Title}
        Planned time: {localTime}
        Product: {content.Product?.Name ?? "Linked product"}
        Product page: {content.DestinationUrl}

        BEFORE PUBLISHING
        [ ] Open and visually inspect all three SVG creative files.
        [ ] Confirm the product name, facts, and image are accurate.
        [ ] Confirm the product-page link opens correctly.
        [ ] Copy the Facebook caption into Facebook.
        [ ] Copy the Instagram caption and hashtags into Instagram.
        [ ] Use the vertical creative for Stories or as the Reel cover.
        [ ] Complete the Story/Reel shot plan if video is being published.
        [ ] Publish or schedule for the planned time shown above.
        [ ] After publishing, verify the live posts and links.

        GHOS records the plan and approval status. It does not publish
        to social accounts automatically.
        """;
    }

    private static string Slugify(string value) =>
        value.Trim().ToLowerInvariant().Replace(' ', '-');
}
