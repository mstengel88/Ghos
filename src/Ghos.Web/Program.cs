using System.Globalization;
using System.Threading.RateLimiting;
using Ghos.Web.Assets;
using Ghos.Web.Auth;
using Ghos.Web.Backups;
using Ghos.Web.Components;
using Ghos.Web.Data;
using Ghos.Web.Dispatch;
using Ghos.Web.DumpSite;
using Ghos.Web.Exports;
using Ghos.Web.Marketing;
using Ghos.Web.ProjectTools;
using Ghos.Web.Shopify;
using Ghos.Web.SmartSearch;
using Ghos.Web.WinterWatch;
using Ghos.Web.WebsiteHealth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var applicationCulture = CultureInfo.GetCultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = applicationCulture;
CultureInfo.DefaultThreadCurrentUICulture = applicationCulture;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.Configure<ShopifyOptions>(
    builder.Configuration.GetSection(ShopifyOptions.SectionName));
builder.Services.AddScoped<ShopifyCredentialStore>();
builder.Services.AddScoped<ShopifyDraftOrderCredentialStore>();
builder.Services.AddHttpClient<ShopifyAccessTokenProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GHOS/1.0");
});
builder.Services.AddHttpClient<ShopifyDraftOrderAccessTokenProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GHOS-Shopify-Drafts/1.0");
});
builder.Services.AddHttpClient<ShopifyAdminClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GHOS/1.0");
});
builder.Services.AddHttpClient<ShopifyDraftOrderClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GHOS-Shopify-Drafts/1.0");
});
builder.Services.AddScoped<ShopifyDraftOrderService>();
builder.Services.AddSingleton<CatalogSyncCoordinator>();
builder.Services.AddScoped<ShopifySyncService>();
builder.Services.AddScoped<SmartProductSearchService>();
builder.Services.AddScoped<SmartSearchTuningService>();
builder.Services.Configure<BackupStatusOptions>(
    builder.Configuration.GetSection(BackupStatusOptions.SectionName));
builder.Services.Configure<WinterWatchAdminOptions>(
    builder.Configuration.GetSection(WinterWatchAdminOptions.SectionName));
builder.Services.AddScoped<WinterWatchCredentialStore>();
builder.Services.AddHttpClient<WinterWatchAdminClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GHOS-WinterWatch-Admin/1.0");
});
builder.Services.AddHttpClient<QuoteDeliveryService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GHOS-Quote/1.0");
});
builder.Services.AddSingleton<QuoteTaxCalculator>();
builder.Services.AddHttpClient<ShopifyQuoteTaxService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "GHOS-Shopify-Tax/1.0");
});
builder.Services.Configure<DispatchQuoteDataOptions>(
    builder.Configuration.GetSection(
        DispatchQuoteDataOptions.SectionName));
builder.Services.AddHttpClient<DispatchQuoteDataSyncService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "GHOS-DispatchQuoteSync/1.0");
});
builder.Services.AddHostedService<CatalogAutomaticSyncService>();
builder.Services.AddScoped<DispatchCredentialStore>();
builder.Services.AddHttpClient<DispatchIntegrationClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GHOS/1.0");
});
builder.Services.AddScoped<DispatchSyncService>();
builder.Services.Configure<DispatchSyncOptions>(
    builder.Configuration.GetSection(
        DispatchSyncOptions.SectionName));
builder.Services.AddHostedService<
    DispatchAutomaticSyncService>();
builder.Services.AddScoped<DumpSiteCredentialStore>();
builder.Services.AddHttpClient<DumpSiteBridgeClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GHOS-DumpSite/1.0");
});
builder.Services.AddScoped<DumpSiteOperatorService>();
builder.Services.Configure<AssetStorageOptions>(
    builder.Configuration.GetSection(AssetStorageOptions.SectionName));
builder.Services.AddScoped<AssetStorageService>();
builder.Services.AddHttpClient<ShopifyAssetImportService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GHOS/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false
});

var dataProtectionKeyPath = builder.Configuration["DataProtection:KeyPath"]
    ?? "/var/lib/ghos/data-protection";

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath))
    .SetApplicationName("GreenHills.GHOS");

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<
    IUserClaimsPrincipalFactory<ApplicationUser>,
    ApplicationUserClaimsPrincipalFactory>();
builder.Services.AddScoped<UserAdministrationService>();
builder.Services.Configure<WebsiteHealthOptions>(
    builder.Configuration.GetSection(WebsiteHealthOptions.SectionName));
builder.Services.AddHttpClient<WebsiteHealthMonitorService>(client =>
{
    client.Timeout = Timeout.InfiniteTimeSpan;
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "GHOS-WebsiteHealth/1.0 (+https://greenhillssupply.com)");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression =
        System.Net.DecompressionMethods.GZip |
        System.Net.DecompressionMethods.Deflate |
        System.Net.DecompressionMethods.Brotli,
    AllowAutoRedirect = false,
    MaxConnectionsPerServer = 2,
});
builder.Services.AddSingleton<WebsiteHealthRunCoordinator>();
builder.Services.AddScoped<WebsiteHealthIssueService>();
builder.Services.AddScoped<WebsiteHealthSettingsService>();
builder.Services.AddHostedService<WebsiteHealthAutomaticCheckService>();
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(2);
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "GHOS.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/access-denied";
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(GhosPolicies.AdministratorOnly,
        policy => policy.RequireRole(GhosRoles.Administrator))
    .AddPolicy(GhosPolicies.Management,
        policy => policy.RequireRole(GhosRoles.Administrator, GhosRoles.Manager))
    .AddPolicy(GhosPolicies.Operations,
        policy => policy.RequireRole(
            GhosRoles.Administrator,
            GhosRoles.Manager,
            GhosRoles.Operations))
    .AddPolicy(GhosPolicies.Marketing,
        policy => policy.RequireRole(
            GhosRoles.Administrator,
            GhosRoles.Manager,
            GhosRoles.Marketing))
    .AddPolicy(GhosPolicies.Assets,
        policy => policy.RequireRole(
            GhosRoles.Administrator,
            GhosRoles.Manager,
            GhosRoles.Operations,
            GhosRoles.Marketing))
    .AddPolicy(GhosPolicies.WebsiteHealth,
        policy => policy.RequireRole(
            GhosRoles.Administrator,
            GhosRoles.Manager,
            GhosRoles.Operations))
    .AddPolicy(GhosPolicies.WebsiteHealthManage,
        policy => policy.RequireRole(
            GhosRoles.Administrator,
            GhosRoles.Manager));

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "text/plain";
        await context.HttpContext.Response.WriteAsync(
            "Too many authentication attempts. Please wait a minute and try again.",
            cancellationToken);
    };
    options.AddPolicy("authentication", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("integrations", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("storefront-search", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapAccountEndpoints();
app.MapAssetEndpoints();
app.MapBackupStatusEndpoints();
app.MapCsvExportEndpoints();
app.MapMarketingCreativeEndpoints();
app.MapMarketingPublicationPackageEndpoints();
app.MapSmartSearchEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await DatabaseInitializer.InitializeAsync(app.Services);

app.Run();
