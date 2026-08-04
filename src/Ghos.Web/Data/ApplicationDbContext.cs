using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Ghos.Web.WebsiteHealth;

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

    public DbSet<QuoteConfiguration> QuoteConfigurations =>
        Set<QuoteConfiguration>();

    public DbSet<QuoteTaxRateCache> QuoteTaxRateCache =>
        Set<QuoteTaxRateCache>();

    public DbSet<QuoteMaterialRule> QuoteMaterialRules =>
        Set<QuoteMaterialRule>();

    public DbSet<QuoteOriginAddress> QuoteOriginAddresses =>
        Set<QuoteOriginAddress>();

    public DbSet<QuoteB2BCompany> QuoteB2BCompanies =>
        Set<QuoteB2BCompany>();

    public DbSet<BackupStatusRecord> BackupStatuses =>
        Set<BackupStatusRecord>();

    public DbSet<WinterWatchConnectionSettings> WinterWatchConnectionSettings =>
        Set<WinterWatchConnectionSettings>();

    public DbSet<MonitoredSite> MonitoredSites => Set<MonitoredSite>();

    public DbSet<WebsiteCheck> WebsiteChecks => Set<WebsiteCheck>();

    public DbSet<WebsiteCheckRun> WebsiteCheckRuns => Set<WebsiteCheckRun>();

    public DbSet<WebsiteHealthIssue> WebsiteHealthIssues =>
        Set<WebsiteHealthIssue>();

    public DbSet<WebsiteHealthMetric> WebsiteHealthMetrics =>
        Set<WebsiteHealthMetric>();

    public DbSet<SmartSearchEvent> SmartSearchEvents =>
        Set<SmartSearchEvent>();

    public DbSet<SmartSearchSynonymRule> SmartSearchSynonymRules =>
        Set<SmartSearchSynonymRule>();

    public DbSet<SmartSearchMerchandisingRule>
        SmartSearchMerchandisingRules =>
            Set<SmartSearchMerchandisingRule>();

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
            product.Property(item => item.CoveragePerOrderUnitSqFt).HasPrecision(18, 4);
            product.Property(item => item.CalculatorUnitLengthInches).HasPrecision(18, 4);
            product.Property(item => item.CalculatorUnitHeightInches).HasPrecision(18, 4);
            product.Property(item => item.SquareFeetPerLayer).HasPrecision(18, 4);

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

        builder.Entity<BackupStatusRecord>(backupStatus =>
        {
            backupStatus.HasIndex(item => item.UpdatedAtUtc);
            backupStatus.Property(item => item.Source).ValueGeneratedNever();
        });

        builder.Entity<ProductVariant>(variant =>
        {
            variant.HasIndex(item => item.ShopifyVariantId).IsUnique();
            variant.Property(item => item.Price).HasPrecision(18, 2);
            variant.Property(item => item.CompareAtPrice).HasPrecision(18, 2);
            variant.Property(item => item.ContractorTier1Price).HasPrecision(18, 2);
            variant.Property(item => item.ContractorTier2Price).HasPrecision(18, 2);
            variant.Property(item => item.CoveragePerOrderUnitSqFt).HasPrecision(18, 4);
            variant.Property(item => item.CalculatorUnitLengthInches).HasPrecision(18, 4);
            variant.Property(item => item.CalculatorUnitHeightInches).HasPrecision(18, 4);
            variant.Property(item => item.SquareFeetPerLayer).HasPrecision(18, 4);

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

        builder.Entity<WinterWatchConnectionSettings>(settings =>
        {
            settings.ToTable("WinterWatchConnectionSettings");
        });

        builder.Entity<MonitoredSite>(site =>
        {
            site.HasIndex(item => item.BaseUrl).IsUnique();
            site.HasIndex(item => new { item.IsEnabled, item.LastCheckedAtUtc });
        });

        builder.Entity<WebsiteCheck>(check =>
        {
            check.HasIndex(item => new { item.MonitoredSiteId, item.Key });
            check.HasIndex(item => new
            {
                item.MonitoredSiteId,
                item.Key,
                item.TargetPath
            }).IsUnique();

            check.HasOne(item => item.MonitoredSite)
                .WithMany(site => site.Checks)
                .HasForeignKey(item => item.MonitoredSiteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WebsiteCheckRun>(run =>
        {
            run.HasIndex(item => new { item.MonitoredSiteId, item.StartedAtUtc });
            run.Property(item => item.Status)
                .HasConversion<string>()
                .HasMaxLength(24);
            run.Property(item => item.OverallScore).HasPrecision(5, 1);
            run.Property(item => item.AvailabilityScore).HasPrecision(5, 1);
            run.Property(item => item.SecurityScore).HasPrecision(5, 1);
            run.Property(item => item.DiscoverabilityScore).HasPrecision(5, 1);
            run.Property(item => item.ContentScore).HasPrecision(5, 1);

            run.HasOne(item => item.MonitoredSite)
                .WithMany(site => site.Runs)
                .HasForeignKey(item => item.MonitoredSiteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WebsiteHealthIssue>(issue =>
        {
            issue.HasIndex(item => new
            {
                item.MonitoredSiteId,
                item.Fingerprint
            }).IsUnique();
            issue.HasIndex(item => new
            {
                item.MonitoredSiteId,
                item.ResolvedAtUtc,
                item.Severity
            });
            issue.Property(item => item.Severity)
                .HasConversion<string>()
                .HasMaxLength(24);
            issue.Property(item => item.AcknowledgedByUserId)
                .HasMaxLength(450);
            issue.Property(item => item.SuppressedByUserId)
                .HasMaxLength(450);
            issue.Property(item => item.Recommendation)
                .HasMaxLength(3000);
            issue.Property(item => item.SuggestedValue)
                .HasMaxLength(6000);
            issue.Property(item => item.CurrentValue)
                .HasMaxLength(6000);
            issue.Property(item => item.EvidenceJson)
                .HasMaxLength(16000);
            issue.Property(item => item.ReviewedValue)
                .HasMaxLength(6000);
            issue.Property(item => item.ReviewedByUserId)
                .HasMaxLength(450);
            issue.Property(item => item.FixLocation)
                .HasMaxLength(1000);
            issue.Property(item => item.FixDocumentationUrl)
                .HasMaxLength(1000);

            issue.HasOne(item => item.MonitoredSite)
                .WithMany(site => site.Issues)
                .HasForeignKey(item => item.MonitoredSiteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WebsiteHealthMetric>(metric =>
        {
            metric.HasIndex(item => new
            {
                item.WebsiteCheckRunId,
                item.Key
            });
            metric.HasIndex(item => new
            {
                item.WebsiteCheckId,
                item.RecordedAtUtc
            });
            metric.Property(item => item.Status)
                .HasConversion<string>()
                .HasMaxLength(24);
            metric.Property(item => item.NumericValue).HasPrecision(14, 2);

            metric.HasOne(item => item.WebsiteCheckRun)
                .WithMany(run => run.Metrics)
                .HasForeignKey(item => item.WebsiteCheckRunId)
                .OnDelete(DeleteBehavior.Cascade);

            metric.HasOne(item => item.WebsiteCheck)
                .WithMany(check => check.Metrics)
                .HasForeignKey(item => item.WebsiteCheckId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SmartSearchEvent>(searchEvent =>
        {
            searchEvent.HasIndex(item => item.SearchedAtUtc);
            searchEvent.HasIndex(item => new
            {
                item.ResultCount,
                item.SearchedAtUtc
            });
            searchEvent.HasIndex(item => item.NormalizedQuery);
            searchEvent.HasIndex(item => item.SelectedProductId);
        });

        builder.Entity<SmartSearchSynonymRule>(rule =>
        {
            rule.HasIndex(item => new
            {
                item.NormalizedPhrase,
                item.NormalizedExpansion
            }).IsUnique();
            rule.HasIndex(item => item.IsActive);
        });

        builder.Entity<SmartSearchMerchandisingRule>(rule =>
        {
            rule.HasIndex(item => new
            {
                item.NormalizedQueryPhrase,
                item.ProductId
            }).IsUnique();
            rule.HasIndex(item => item.IsActive);
            rule.HasIndex(item => item.ProductId);
            rule.HasOne(item => item.Product)
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
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
            quote.Property(item => item.Audience)
                .HasConversion<string>()
                .HasMaxLength(24);
            quote.Property(item => item.ContractorTier)
                .HasConversion<string>()
                .HasMaxLength(24);
            quote.Property(item => item.Subtotal).HasPrecision(18, 2);
            quote.Property(item => item.DeliveryAmount).HasPrecision(18, 2);
            quote.Property(item => item.CalculatedDeliveryAmount).HasPrecision(18, 2);
            quote.Property(item => item.CustomDeliveryAmount).HasPrecision(18, 2);
            quote.Property(item => item.RatePerMinute).HasPrecision(10, 2);
            quote.Property(item => item.ShippingQuantity).HasPrecision(18, 3);
            quote.Property(item => item.ShippingRate).HasPrecision(18, 2);
            quote.Property(item => item.TaxRate).HasPrecision(8, 6);
            quote.Property(item => item.TaxRateLabel).HasMaxLength(80);
            quote.Property(item => item.TaxAmount).HasPrecision(18, 2);
            quote.Property(item => item.Total).HasPrecision(18, 2);
        });

        builder.Entity<CustomerQuoteLine>(line =>
        {
            line.HasIndex(item => new { item.CustomerQuoteId, item.SortOrder });
            line.Property(item => item.Quantity).HasPrecision(18, 3);
            line.Property(item => item.UnitPrice).HasPrecision(18, 2);
            line.Property(item => item.LineTotal).HasPrecision(18, 2);
            line.Property(item => item.Audience)
                .HasConversion<string>()
                .HasMaxLength(24);
            line.Property(item => item.ContractorTier)
                .HasConversion<string>()
                .HasMaxLength(24);

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

        builder.Entity<QuoteConfiguration>(configuration =>
        {
            configuration.ToTable("QuoteConfiguration");
            configuration.Property(item => item.TestFlatRate).HasPrecision(18, 2);
            configuration.Property(item => item.DefaultTaxRate).HasPrecision(8, 6);
            configuration.Property(item => item.DefaultRatePerMinute).HasPrecision(10, 2);
            configuration.Property(item => item.MaximumDeliveryRadiusMiles)
                .HasPrecision(10, 2);
        });

        builder.Entity<QuoteTaxRateCache>(cache =>
        {
            cache.ToTable("QuoteTaxRateCache");
            cache.HasIndex(item => item.CacheKey).IsUnique();
            cache.HasIndex(item => item.ExpiresAtUtc);
            cache.Property(item => item.Rate).HasPrecision(8, 6);
            cache.Property(item => item.SampleTaxableAmount)
                .HasPrecision(18, 2);
            cache.Property(item => item.ShopifyTotalTax)
                .HasPrecision(18, 2);
        });

        builder.Entity<QuoteMaterialRule>(rule =>
        {
            rule.HasIndex(item => item.SkuPrefix).IsUnique();
            rule.Property(item => item.TruckCapacity).HasPrecision(18, 3);
        });

        builder.Entity<QuoteOriginAddress>(origin =>
        {
            origin.HasIndex(item => item.Label).IsUnique();
        });

        builder.Entity<QuoteB2BCompany>(company =>
        {
            company.HasIndex(item => item.ExternalId).IsUnique();
            company.HasIndex(item => item.ShopifyCompanyId);
            company.HasIndex(item => item.CompanyName);
            company.Property(item => item.ContractorTier)
                .HasConversion<string>()
                .HasMaxLength(24);
        });
    }
}
