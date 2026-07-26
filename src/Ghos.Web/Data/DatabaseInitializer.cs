using Ghos.Web.Auth;
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
            CreateCategory("Sand", "sand", 50),
            CreateCategory("Bagged Materials", "bagged-materials", 60),
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
    }

    private static ProductCategory CreateCategory(string name, string slug, int sortOrder) =>
        new()
        {
            Name = name,
            Slug = slug,
            SortOrder = sortOrder
        };
}
