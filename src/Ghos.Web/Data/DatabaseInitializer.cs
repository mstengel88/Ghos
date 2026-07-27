using Ghos.Web.Auth;
using Ghos.Web.ProjectTools;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var roleName in GhosRoles.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
            {
                var details = string.Join("; ", result.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Unable to create the '{roleName}' role: {details}");
            }
        }

        var categories = new[]
        {
            CreateCategory("Aggregate", "aggregate", 10),
            CreateCategory("Mulch", "mulch", 20),
            CreateCategory("Topsoil & Soil", "topsoil-soil", 30),
            CreateCategory("Decorative Stone", "decorative-stone", 40),
            CreateCategory("Boulders & Outcroppings", "boulders-outcroppings", 45),
            CreateCategory("Sand", "sand", 50),
            CreateCategory("Bagged Materials", "bagged-materials", 60),
            CreateCategory("Landscape Essentials", "landscape-essentials", 65),
            CreateCategory("Outdoor Living", "outdoor-living", 70),
            CreateCategory("Tools & Equipment", "tools-equipment", 80),
            CreateCategory("Ice Melt", "ice-melt", 90),
            CreateCategory("Shopify Import", "shopify-import", 1000)
        };

        var existingCategorySlugs = await dbContext.ProductCategories
            .Select(category => category.Slug)
            .ToListAsync();
        var missingCategories = categories
            .Where(category => !existingCategorySlugs.Contains(
                category.Slug,
                StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (missingCategories.Count > 0)
        {
            dbContext.ProductCategories.AddRange(missingCategories);
            await dbContext.SaveChangesAsync();
        }

        await SeedTomorrowMaterialMondayAsync(dbContext);
        await SeedMaterialProfilesAsync(dbContext);
        await SeedQuoteConfigurationAsync(dbContext);
    }

    private static async Task SeedQuoteConfigurationAsync(
        ApplicationDbContext dbContext)
    {
        if (!await dbContext.QuoteConfigurations.AnyAsync())
        {
            dbContext.QuoteConfigurations.Add(new QuoteConfiguration());
        }

        var existingPrefixes = await dbContext.QuoteMaterialRules
            .Select(rule => rule.SkuPrefix)
            .ToListAsync();
        var rules = new[]
        {
            new QuoteMaterialRule
            {
                SkuPrefix = "100",
                MaterialName = "Aggregate",
                TruckCapacity = 22m,
                VendorSource = "Aggregate",
                SortOrder = 100
            },
            new QuoteMaterialRule
            {
                SkuPrefix = "300",
                MaterialName = "Mulch",
                TruckCapacity = 25m,
                VendorSource = "Mulch",
                SortOrder = 300
            },
            new QuoteMaterialRule
            {
                SkuPrefix = "400",
                MaterialName = "Soil",
                TruckCapacity = 25m,
                VendorSource = "Soil",
                SortOrder = 400
            },
            new QuoteMaterialRule
            {
                SkuPrefix = "499",
                MaterialName = "Field Run",
                TruckCapacity = 20m,
                VendorSource = "Field Run",
                SortOrder = 499
            }
        };
        dbContext.QuoteMaterialRules.AddRange(
            rules.Where(rule => !existingPrefixes.Contains(rule.SkuPrefix)));

        if (!await dbContext.QuoteOriginAddresses.AnyAsync())
        {
            dbContext.QuoteOriginAddresses.Add(new QuoteOriginAddress
            {
                Label = "Menomonee Falls",
                Address =
                    "W185 N7487 Narrow Ln, Menomonee Falls, WI 53051",
                IsDefault = true
            });
        }

        await dbContext.SaveChangesAsync();
    }

    private static ProductCategory CreateCategory(string name, string slug, int sortOrder) =>
        new()
        {
            Name = name,
            Slug = slug,
            SortOrder = sortOrder
        };

    private static async Task SeedMaterialProfilesAsync(
        ApplicationDbContext dbContext)
    {
        var products = await dbContext.Products
            .Where(product =>
                product.ShopifyHandle != null &&
                product.MaterialProfile == null)
            .ToListAsync();

        foreach (var product in products)
        {
            if (!GreenHillsMaterialProfiles.ByShopifyHandle.TryGetValue(
                    product.ShopifyHandle!,
                    out var profile))
            {
                continue;
            }

            dbContext.ProductMaterialProfiles.Add(new ProductMaterialProfile
            {
                ProductId = product.Id,
                SoldBy = profile.SoldBy,
                TonsPerCubicYard = profile.TonsPerYard,
                OrderIncrement = 1m
            });
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedTomorrowMaterialMondayAsync(
        ApplicationDbContext dbContext)
    {
        const string contentSlug = "material-monday-1-stone-2026-07-27";

        if (await dbContext.MarketingContentPackages.AnyAsync(content =>
            content.Slug == contentSlug))
        {
            return;
        }

        var product = await dbContext.Products
            .Include(item => item.AssetLinks)
                .ThenInclude(link => link.DigitalAsset)
            .SingleOrDefaultAsync(item => item.Slug == "1-stone");

        if (product is null)
        {
            return;
        }

        var primaryAsset = product.AssetLinks
            .Where(link =>
                link.IsPrimary &&
                link.DigitalAsset.Kind == AssetKind.Image &&
                link.DigitalAsset.Status == AssetStatus.Approved)
            .Select(link => link.DigitalAsset)
            .FirstOrDefault();
        var now = DateTime.UtcNow;

        dbContext.MarketingContentPackages.Add(new MarketingContentPackage
        {
            Slug = contentSlug,
            Title = "Material Monday — #1 Stone",
            Series = "Material Monday",
            TemplateKey = "material-monday",
            Status = MarketingContentStatus.ReadyForReview,
            ScheduledForUtc = new DateTime(2026, 7, 27, 13, 0, 0, DateTimeKind.Utc),
            ProductId = product.Id,
            DigitalAssetId = primaryAsset?.Id,
            Headline = "#1 STONE",
            Subheadline = "Clean crushed limestone · 3/8″–1″",
            AlternateName = "Also known as #57 Stone",
            FactItems = string.Join(
                Environment.NewLine,
                "Drainage",
                "Pipe bedding",
                "Backfill",
                "Landscape features"),
            FacebookCaption =
                """
                🪨 Material Monday: #1 Stone

                Need a dependable material for drainage or structural support? Our #1 Stone is a clean crushed limestone ranging from 3/8″ to 1″.

                It’s a versatile choice for:
                ✔ French drains and septic systems
                ✔ Pipe bedding and backfill
                ✔ Garden beds and walkways
                ✔ Other construction and landscape projects

                Not sure how much you need? Tell us about your project and our team will help you plan it.

                Learn more: https://greenhillssupply.com/products/1-stone
                """,
            InstagramCaption =
                """
                Material Monday 🪨

                #1 Stone—also known as #57 Stone—is a clean crushed limestone ranging from 3/8″ to 1″.

                Use it for drainage, pipe bedding, backfill, garden beds, walkways, and more.

                Planning a project? Our team can help you choose the right material and estimate how much you need.

                Learn more at the link in our bio.
                """,
            StoryPrompt =
                "What are you building with #1 Stone? | Drainage | Landscape",
            ReelScript =
                """
                HOOK — On-screen text: “Need stone for drainage?”

                SHOW — Close-up of #1 Stone, then the loader filling a truck.

                VOICEOVER — “Our #1 Stone is a clean crushed limestone ranging from three-eighths of an inch to one inch. It’s a dependable choice for French drains, septic systems, pipe bedding, backfill, and landscape features.”

                CLOSE — On-screen text: “#1 Stone · Pickup or delivery · Green Hills Supply”

                CTA — “Tell us about your project and we’ll help you plan the right amount.”
                """,
            Hashtags =
                "#GreenHillsSupply #MaterialMonday #1Stone #57Stone #LandscapeSupply #Drainage #WisconsinLandscaping",
            CallToAction = "Plan your project with Green Hills Supply",
            DestinationUrl = "https://greenhillssupply.com/products/1-stone",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await dbContext.SaveChangesAsync();
    }
}
