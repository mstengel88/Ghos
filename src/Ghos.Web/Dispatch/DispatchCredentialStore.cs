using Ghos.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Ghos.Web.Dispatch;

public sealed record DispatchCredentials(string BaseUrl, string IntegrationSecret);

public sealed class DispatchCredentialStore
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IDataProtector _protector;
    private readonly DispatchSyncOptions _options;

    public DispatchCredentialStore(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<DispatchSyncOptions> options)
    {
        _dbContextFactory = dbContextFactory;
        _protector = dataProtectionProvider.CreateProtector(
            "GreenHills.GHOS.Dispatch.IntegrationSecret.v1");
        _options = options.Value;
    }

    public bool UsesServerManagedCredentials =>
        !string.IsNullOrWhiteSpace(_options.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_options.IntegrationSecret);

    public string? ServerManagedFingerprint =>
        UsesServerManagedCredentials
            ? CreateFingerprint(_options.IntegrationSecret)
            : null;

    public async Task<bool> HasCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        if (UsesServerManagedCredentials)
        {
            return true;
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.DispatchConnectionSettings
            .AnyAsync(cancellationToken);
    }

    public async Task<DispatchCredentials?> GetAsync(
        CancellationToken cancellationToken = default)
    {
        if (UsesServerManagedCredentials)
        {
            return new DispatchCredentials(
                NormalizeBaseUrl(_options.BaseUrl),
                _options.IntegrationSecret.Trim());
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await dbContext.DispatchConnectionSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            return null;
        }

        try
        {
            return new DispatchCredentials(
                settings.BaseUrl,
                _protector.Unprotect(settings.EncryptedIntegrationSecret));
        }
        catch (Exception exception)
        {
            throw new DispatchConnectionException(
                "GHOS could not decrypt the saved dispatch secret. Re-enter it in Dispatch Connection settings.",
                exception);
        }
    }

    public async Task SaveAsync(
        string baseUrl,
        string integrationSecret,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var normalizedUrl = NormalizeBaseUrl(baseUrl);
        if (string.IsNullOrWhiteSpace(integrationSecret))
        {
            throw new DispatchConnectionException(
                "Enter the dispatch integration secret.");
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await dbContext.DispatchConnectionSettings
            .SingleOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new DispatchConnectionSettings();
            dbContext.DispatchConnectionSettings.Add(settings);
        }

        settings.BaseUrl = normalizedUrl;
        settings.EncryptedIntegrationSecret =
            _protector.Protect(integrationSecret.Trim());
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
            throw new DispatchConnectionException(
                "Enter the secure HTTPS address for the dispatch app.");
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    public static string CreateFingerprint(string integrationSecret)
    {
        var normalized = integrationSecret.Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
