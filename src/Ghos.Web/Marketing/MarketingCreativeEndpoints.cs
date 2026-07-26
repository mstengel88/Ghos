using System.Security;
using System.Text;
using Ghos.Web.Assets;
using Ghos.Web.Auth;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.Marketing;

public static class MarketingCreativeEndpoints
{
    public static IEndpointRouteBuilder MapMarketingCreativeEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/marketing-content/{contentId:guid}/creative/{templateKey}.svg",
                DownloadCreativeAsync)
            .RequireAuthorization(GhosPolicies.Marketing);

        return endpoints;
    }

    private static async Task<IResult> DownloadCreativeAsync(
        Guid contentId,
        string templateKey,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        AssetStorageService storageService,
        CancellationToken cancellationToken)
    {
        var template = MarketingTemplateCatalog.All.SingleOrDefault(item =>
            item.Key.Equals(templateKey, StringComparison.OrdinalIgnoreCase));
        if (template is null)
        {
            return Results.NotFound();
        }

        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var content = await dbContext.MarketingContentPackages
            .AsNoTracking()
            .Include(item => item.DigitalAsset)
            .SingleOrDefaultAsync(item => item.Id == contentId, cancellationToken);
        if (content?.DigitalAsset is null ||
            content.DigitalAsset.Kind != AssetKind.Image ||
            content.DigitalAsset.Status != AssetStatus.Approved)
        {
            return Results.NotFound();
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

        var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var imageData = Convert.ToBase64String(imageBytes);
        var svg = RenderSvg(
            content,
            template,
            $"data:{content.DigitalAsset.ContentType};base64,{imageData}");
        var fileName =
            $"{content.Slug}-{template.Format.Replace(' ', '-').ToLowerInvariant()}.svg";

        return Results.File(
            Encoding.UTF8.GetBytes(svg),
            "image/svg+xml",
            fileName);
    }

    private static string RenderSvg(
        MarketingContentPackage content,
        MarketingTemplateDefinition template,
        string imageData)
    {
        var width = template.Width;
        var height = template.Height;
        var vertical = height > width;
        var headlineY = vertical ? 1230 : 690;
        var subheadlineY = headlineY + (vertical ? 120 : 105);
        var alternateY = headlineY - (vertical ? 110 : 90);
        var footerHeight = vertical ? 150 : 125;
        var facts = (content.FactItems ?? string.Empty)
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Take(4)
            .ToList();
        var factMarkup = vertical
            ? $"""
               <rect x="75" y="{subheadlineY + 90}" width="{width - 150}" height="120" rx="12" fill="#142116" fill-opacity=".78" />
               <rect x="75" y="{subheadlineY + 90}" width="12" height="120" fill="#9BC623" />
               <text x="115" y="{subheadlineY + 162}" class="cta">{Escape(content.CallToAction)}</text>
               """
            : string.Join(
                Environment.NewLine,
                facts.Select((fact, index) =>
                {
                    var x = index % 2 == 0 ? 75 : 560;
                    var y = 875 + (index / 2 * 58);
                    return $"<text x=\"{x}\" y=\"{y}\" class=\"fact\"><tspan class=\"check\">✓</tspan> {Escape(fact)}</text>";
                }));

        return $$"""
            <svg xmlns="http://www.w3.org/2000/svg"
                 width="{{width}}"
                 height="{{height}}"
                 viewBox="0 0 {{width}} {{height}}">
              <defs>
                <linearGradient id="shade" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stop-color="#0f1410" stop-opacity=".05" />
                  <stop offset="43%" stop-color="#0f1410" stop-opacity=".3" />
                  <stop offset="100%" stop-color="#0f1410" stop-opacity=".98" />
                </linearGradient>
                <style>
                  text { font-family: Arial, Helvetica, sans-serif; }
                  .series-small { font-size: {{GetSize(vertical, 28, 22)}}px; font-weight: 800; letter-spacing: 5px; fill: #172019; }
                  .series-large { font-size: {{GetSize(vertical, 52, 43)}}px; font-weight: 950; fill: #172019; }
                  .guide { font-size: {{GetSize(vertical, 22, 19)}}px; font-weight: 800; letter-spacing: 4px; fill: white; }
                  .alternate { font-size: {{GetSize(vertical, 27, 24)}}px; font-weight: 850; letter-spacing: 2px; fill: #bfe057; }
                  .headline { font-size: {{GetSize(vertical, 126, 116)}}px; font-weight: 950; letter-spacing: -7px; fill: white; }
                  .subheadline { font-size: {{GetSize(vertical, 32, 29)}}px; font-weight: 700; fill: #eef1ee; }
                  .fact { font-size: 27px; font-weight: 750; fill: white; }
                  .check { fill: #9BC623; }
                  .cta { font-size: 29px; font-weight: 800; fill: white; }
                  .brand { font-size: {{GetSize(vertical, 27, 25)}}px; font-weight: 950; letter-spacing: 2px; fill: #172019; }
                  .tagline { font-size: {{GetSize(vertical, 18, 17)}}px; fill: #172019; }
                  .logo { font-size: {{GetSize(vertical, 28, 25)}}px; font-weight: 950; fill: #172019; }
                </style>
              </defs>
              <image href="{{imageData}}"
                     x="0" y="0" width="{{width}}" height="{{height}}"
                     preserveAspectRatio="xMidYMid slice" />
              <rect x="0" y="0" width="{{width}}" height="{{height}}" fill="url(#shade)" />

              <path d="M0 0 H{{(vertical ? 500 : 420)}} L{{(vertical ? 455 : 385)}} {{(vertical ? 225 : 180)}} H0 Z" fill="#9BC623" />
              <text x="55" y="{{(vertical ? 80 : 62)}}" class="series-small">MATERIAL</text>
              <text x="55" y="{{(vertical ? 142 : 116)}}" class="series-large">MONDAY</text>
              <text x="{{width - 55}}" y="{{(vertical ? 70 : 58)}}" text-anchor="end" class="guide">MATERIAL GUIDE · 001</text>

              <text x="75" y="{{alternateY}}" class="alternate">{{Escape(content.AlternateName)}}</text>
              <text x="70" y="{{headlineY}}" class="headline">{{Escape(content.Headline)}}</text>
              <text x="75" y="{{subheadlineY}}" class="subheadline">{{Escape(content.Subheadline)}}</text>
              {{factMarkup}}

              <rect x="0" y="{{height - footerHeight}}" width="{{width}}" height="{{footerHeight}}" fill="#9BC623" />
              <rect x="55" y="{{height - footerHeight + 28}}" width="{{footerHeight - 56}}" height="{{footerHeight - 56}}" rx="14" fill="none" stroke="#172019" stroke-width="5" />
              <text x="{{footerHeight / 2 + 27}}" y="{{height - footerHeight / 2 + 11}}" text-anchor="middle" class="logo">GH</text>
              <text x="{{footerHeight + 25}}" y="{{height - footerHeight + 58}}" class="brand">GREEN HILLS SUPPLY</text>
              <text x="{{footerHeight + 25}}" y="{{height - footerHeight + 93}}" class="tagline">Built for Contractors. Trusted by Homeowners.</text>
            </svg>
            """;
    }

    private static int GetSize(bool vertical, int verticalSize, int squareSize) =>
        vertical ? verticalSize : squareSize;

    private static string Escape(string? value) =>
        SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
}
