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

    public DbSet<ShopifyCollection> ShopifyCollections => Set<ShopifyCollection>();

    public DbSet<ProductShopifyCollection> ProductShopifyCollections => Set<ProductShopifyCollection>();

    public DbSet<DigitalAsset> DigitalAssets => Set<DigitalAsset>();

    public DbSet<AssetProductLink> AssetProductLinks => Set<AssetProductLink>();

    public DbSet<BulkOperation> BulkOperations => Set<BulkOperation>();

    public DbSet<MarketingContentPackage> MarketingContentPackages =>
        Set<MarketingContentPackage>();

    public DbSet<MarketingPerformanceSnapshot> MarketingPerformanceSnapshots =>
        Set<MarketingPerformanceSnapshot>();

    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();

    public DbSet<Delivery> Deliveries => Set<Delivery>();

    public DbSet<DispatchConnectionSettings> DispatchConnectionSettings =>
        Set<DispatchConnectionSettings>();

    public DbSet<DumpSiteConnectionSettings> DumpSiteConnectionSettings =>
        Set<DumpSiteConnectionSettings>();

    public DbSet<ProductMaterialProfile> ProductMaterialProfiles =>
        Set<ProductMaterialProfile>();

    public DbSet<CustomerQuote> CustomerQuotes => Set<CustomerQuote>();

    public DbSet<CustomerQuoteLine> CustomerQuoteLines => Set<CustomerQuoteLine>();

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

        builder.Entity<ShopifyCollection>(collection =>
        {
            collection.HasIndex(item => item.ShopifyCollectionId).IsUnique();
            collection.HasIndex(item => item.Handle).IsUnique();
            collection.HasIndex(item => item.Title);
        });

        builder.Entity<ProductShopifyCollection>(link =>
        {
            link.HasKey(item => new { item.ProductId, item.ShopifyCollectionId });
            link.HasIndex(item => item.ShopifyCollectionId);

            link.HasOne(item => item.Product)
                .WithMany(product => product.ShopifyCollectionLinks)
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            link.HasOne(item => item.ShopifyCollection)
                .WithMany(collection => collection.ProductLinks)
                .HasForeignKey(item => item.ShopifyCollectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DigitalAsset>(asset =>
        {
            asset.HasIndex(item => item.Sha256Hash).IsUnique();
            asset.HasIndex(item => item.CreatedAtUtc);
            asset.HasIndex(item => new { item.Status, item.Kind });
            asset.HasIndex(item => item.SourceUrl)
                .IsUnique()
                .HasFilter("\"SourceUrl\" IS NOT NULL");
            asset.Property(item => item.Kind).HasConversion<string>().HasMaxLength(24);
            asset.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
            asset.Property(item => item.Source).HasConversion<string>().HasMaxLength(24);
        });

        builder.Entity<AssetProductLink>(link =>
        {
            link.HasKey(item => new { item.DigitalAssetId, item.ProductId });
            link.HasIndex(item => item.ProductId);
            link.HasIndex(item => new { item.ProductId, item.IsPrimary })
                .IsUnique()
                .HasFilter("\"IsPrimary\" = TRUE");
            link.Property(item => item.PrimaryAssignedByUserId)
                .HasMaxLength(450);

            link.HasOne(item => item.DigitalAsset)
                .WithMany(asset => asset.ProductLinks)
                .HasForeignKey(item => item.DigitalAssetId)
                .OnDelete(DeleteBehavior.Cascade);

            link.HasOne(item => item.Product)
                .WithMany(product => product.AssetLinks)
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BulkOperation>(operation =>
        {
            operation.HasIndex(item => item.PerformedAtUtc);
            operation.HasIndex(item => new { item.TargetType, item.PerformedAtUtc });
        });

        builder.Entity<MarketingContentPackage>(content =>
        {
            content.HasIndex(item => item.Slug).IsUnique();
            content.HasIndex(item => item.ScheduledForUtc);
            content.HasIndex(item => new { item.Status, item.ScheduledForUtc });
            content.Property(item => item.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            content.HasOne(item => item.Product)
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.SetNull);

            content.HasOne(item => item.DigitalAsset)
                .WithMany()
                .HasForeignKey(item => item.DigitalAssetId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<MarketingPerformanceSnapshot>(snapshot =>
        {
            snapshot.HasIndex(item => new
            {
                item.MarketingContentPackageId,
                item.CapturedAtUtc
            }).HasDatabaseName(
                "IX_MarketingPerformance_Content_Captured");
            snapshot.Property(item => item.Revenue).HasPrecision(18, 2);

            snapshot.HasOne(item => item.MarketingContentPackage)
                .WithMany(content => content.PerformanceSnapshots)
                .HasForeignKey(item => item.MarketingContentPackageId)
                .HasConstraintName("FK_MarketingPerformance_Content")
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SalesOrder>(order =>
        {
            order.HasIndex(item => item.ExternalKey).IsUnique();
            order.HasIndex(item => item.OrderNumber);
            order.HasIndex(item => new { item.Status, item.UpdatedAtUtc });
            order.Property(item => item.Source)
                .HasConversion<string>()
                .HasMaxLength(24);
            order.Property(item => item.Status)
                .HasConversion<string>()
                .HasMaxLength(24);
        });

        builder.Entity<Delivery>(delivery =>
        {
            delivery.HasIndex(item => item.ExternalDispatchId).IsUnique();
            delivery.HasIndex(item => item.SalesOrderId);
            delivery.HasIndex(item => item.ScheduledForUtc);
            delivery.HasIndex(item => new
            {
                item.Status,
                item.ScheduledForUtc
            });
            delivery.Property(item => item.Status)
                .HasConversion<string>()
                .HasMaxLength(24);
            delivery.Property(item =>
                    item.ReconciledStatusOverride)
                .HasConversion<string>()
                .HasMaxLength(24);
            delivery.Property(item => item.TravelMinutes)
                .HasPrecision(10, 2);
            delivery.Property(item => item.TravelMiles)
                .HasPrecision(10, 2);

            delivery.HasOne(item => item.SalesOrder)
                .WithMany(order => order.Deliveries)
                .HasForeignKey(item => item.SalesOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DispatchConnectionSettings>(settings =>
        {
            settings.ToTable("DispatchConnectionSettings");
        });

        builder.Entity<DumpSiteConnectionSettings>(settings =>
        {
            settings.ToTable("DumpSiteConnectionSettings");
        });

        builder.Entity<ProductMaterialProfile>(profile =>
        {
            profile.HasIndex(item => item.ProductId).IsUnique();
            profile.Property(item => item.SoldBy)
                .HasConversion<string>()
                .HasMaxLength(24);
            profile.Property(item => item.TonsPerCubicYard).HasPrecision(10, 4);
            profile.Property(item => item.OrderIncrement).HasPrecision(10, 2);

            profile.HasOne(item => item.Product)
                .WithOne(product => product.MaterialProfile)
                .HasForeignKey<ProductMaterialProfile>(item => item.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CustomerQuote>(quote =>
        {
            quote.HasIndex(item => item.QuoteNumber).IsUnique();
            quote.HasIndex(item => new { item.Status, item.UpdatedAtUtc });
            quote.Property(item => item.Status)
                .HasConversion<string>()
                .HasMaxLength(32);
            quote.Property(item => item.Subtotal).HasPrecision(18, 2);
            quote.Property(item => item.DeliveryAmount).HasPrecision(18, 2);
            quote.Property(item => item.TaxRate).HasPrecision(8, 6);
            quote.Property(item => item.TaxAmount).HasPrecision(18, 2);
            quote.Property(item => item.Total).HasPrecision(18, 2);
        });

        builder.Entity<CustomerQuoteLine>(line =>
        {
            line.HasIndex(item => new { item.CustomerQuoteId, item.SortOrder });
            line.Property(item => item.Quantity).HasPrecision(18, 3);
            line.Property(item => item.UnitPrice).HasPrecision(18, 2);
            line.Property(item => item.LineTotal).HasPrecision(18, 2);

            line.HasOne(item => item.CustomerQuote)
                .WithMany(quote => quote.Lines)
                .HasForeignKey(item => item.CustomerQuoteId)
                .OnDelete(DeleteBehavior.Cascade);

            line.HasOne(item => item.Product)
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.SetNull);

            line.HasOne(item => item.ProductVariant)
                .WithMany()
                .HasForeignKey(item => item.ProductVariantId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
