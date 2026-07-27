using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Ghos.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.DumpSite;

public sealed record DumpSiteCredentials(
    string BridgeApiBaseUrl,
    string SharedSecret,
    string BridgeId);

public sealed record DumpSiteConfiguration(
    DumpSiteConnectionSettings Settings,
    DumpSiteCredentials Credentials);

public sealed class DumpSiteCredentialStore
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IDataProtector _protector;

    public DumpSiteCredentialStore(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IDataProtectionProvider dataProtectionProvider)
    {
        _dbContextFactory = dbContextFactory;
        _protector = dataProtectionProvider.CreateProtector(
            "GreenHills.GHOS.DumpSite.SharedSecret.v1");
    }

    public async Task<bool> HasCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.DumpSiteConnectionSettings
            .AnyAsync(cancellationToken);
    }

    public async Task<DumpSiteConnectionSettings?> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.DumpSiteConnectionSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<DumpSiteConfiguration?> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        if (settings is null)
        {
            return null;
        }

        try
        {
            return new DumpSiteConfiguration(
                settings,
                new DumpSiteCredentials(
                    settings.BridgeApiBaseUrl,
                    _protector.Unprotect(
                        settings.EncryptedSharedSecret),
                    settings.BridgeId));
        }
        catch (Exception exception)
        {
            throw new DumpSiteConnectionException(
                "GHOS could not decrypt the saved Dumpsite secret. Re-enter it in Dumpsite Connection settings.",
                exception);
        }
    }

    public async Task SaveAsync(
        DumpSiteConnectionInput input,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = NormalizeBaseUrl(input.BridgeApiBaseUrl);
        var bridgeId = NormalizeBridgeId(input.BridgeId);
        var sharedSecret = input.SharedSecret.Trim();
        if (sharedSecret.Length < 24)
        {
            throw new DumpSiteConnectionException(
                "Use a bridge secret with at least 24 characters.");
        }

        var itemMappings = NormalizeJsonObject(
            input.ItemMappingsJson,
            "item mappings");
        var companyMappings = NormalizeJsonObject(
            input.CompanyMappingsJson,
            "company mappings");

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await dbContext.DumpSiteConnectionSettings
            .SingleOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new DumpSiteConnectionSettings();
            dbContext.DumpSiteConnectionSettings.Add(settings);
        }

        settings.BridgeApiBaseUrl = baseUrl;
        settings.EncryptedSharedSecret =
            _protector.Protect(sharedSecret);
        settings.BridgeId = bridgeId;
        settings.ItemMappingsJson = itemMappings;
        settings.CompanyMappingsJson = companyMappings;
        settings.CounterpointLocation = Clean(
            input.CounterpointLocation,
            20,
            "Counterpoint location");
        settings.CounterpointStation = Clean(
            input.CounterpointStation,
            20,
            "Counterpoint station");
        settings.CounterpointDrawer = Clean(
            input.CounterpointDrawer,
            20,
            "Counterpoint drawer");
        settings.CounterpointSalesRep = Clean(
            input.CounterpointSalesRep,
            30,
            "Counterpoint sales rep");
        settings.LastHealthCheckAtUtc = DateTime.UtcNow;
        settings.LastHealthCheckSucceeded = true;
        settings.LastHealthCheckMessage =
            "The Supabase Dumpsite bridge accepted the GHOS connection.";
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedByUserId = userId;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static string NormalizeBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new DumpSiteConnectionException(
                "Enter the secure HTTPS address for the Dumpsite bridge API.");
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public static string NormalizeBridgeId(string bridgeId)
    {
        var value = bridgeId.Trim();
        if (value.Length is < 3 or > 100 ||
            value.Any(character =>
                !(char.IsLetterOrDigit(character) ||
                  character is '-' or '_' or '.')))
        {
            throw new DumpSiteConnectionException(
                "Use a bridge ID containing only letters, numbers, dashes, underscores, or periods.");
        }

        return value;
    }

    public static string NormalizeJsonObject(
        string json,
        string fieldName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new DumpSiteConnectionException(
                    $"The {fieldName} must be a JSON object.");
            }

            return JsonSerializer.Serialize(
                document.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException exception)
        {
            throw new DumpSiteConnectionException(
                $"The {fieldName} JSON is not valid: {exception.Message}",
                exception);
        }
    }

    private static string Clean(
        string value,
        int maxLength,
        string label)
    {
        var result = value.Trim();
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new DumpSiteConnectionException(
                $"{label} is required.");
        }

        return result[..Math.Min(result.Length, maxLength)];
    }
}

public sealed class DumpSiteConnectionInput
{
    [Required]
    [Url]
    public string BridgeApiBaseUrl { get; set; } = string.Empty;

    [Required]
    [MinLength(24)]
    public string SharedSecret { get; set; } = string.Empty;

    [Required]
    [MinLength(3)]
    [MaxLength(100)]
    public string BridgeId { get; set; } =
        "ghos-dump-site-operator";

    [Required]
    public string ItemMappingsJson { get; set; } = "{}";

    [Required]
    public string CompanyMappingsJson { get; set; } = "{}";

    [Required]
    public string CounterpointLocation { get; set; } = "101";

    [Required]
    public string CounterpointStation { get; set; } = "201-01";

    [Required]
    public string CounterpointDrawer { get; set; } = "201-01";

    [Required]
    public string CounterpointSalesRep { get; set; } = "EC_SHOPIFY";
}
