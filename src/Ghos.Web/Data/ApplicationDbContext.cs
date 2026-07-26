using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductAlternateName> ProductAlternateNames => Set<ProductAlternateName>();

    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    public DbSet<ShopifySyncRun> ShopifySyncRuns => Set<ShopifySyncRun>();

    public DbSet<ShopifyConnectionSettings> ShopifyConnectionSettings => Set<ShopifyConnectionSettings>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ProductCategory>(category =>
        {
            category.HasIndex(item => item.Name).IsUnique();
            category.HasIndex(item => item.Slug).IsUnique();
        });

        builder.Entity<Product>(product =>
        {
            product.HasIndex(item => item.Name);
            product.HasIndex(item => item.Slug).IsUnique();
            product.HasIndex(item => item.ShopifyProductId)
                .IsUnique()
                .HasFilter("\"ShopifyProductId\" IS NOT NULL");
            product.HasIndex(item => item.ShopifyHandle)
                .HasFilter("\"ShopifyHandle\" IS NOT NULL");
            product.HasIndex(item => item.ProductCode)
                .IsUnique()
                .HasFilter("\"ProductCode\" IS NOT NULL");
            product.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);

            product.HasOne(item => item.ProductCategory)
                .WithMany(category => category.Products)
                .HasForeignKey(item => item.ProductCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductAlternateName>(alternateName =>
        {
            alternateName.HasIndex(item => item.NormalizedName).IsUnique();

            alternateName.HasOne(item => item.Product)
                .WithMany(product => product.AlternateNames)
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProductVariant>(variant =>
        {
            variant.HasIndex(item => item.ShopifyVariantId).IsUnique();
            variant.Property(item => item.Price).HasPrecision(18, 2);
            variant.Property(item => item.CompareAtPrice).HasPrecision(18, 2);

            variant.HasOne(item => item.Product)
                .WithMany(product => product.Variants)
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ShopifySyncRun>(syncRun =>
        {
            syncRun.HasIndex(item => item.StartedAtUtc);
        });

        builder.Entity<ShopifyConnectionSettings>(settings =>
        {
            settings.ToTable("ShopifyConnectionSettings");
        });
    }
}
