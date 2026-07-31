using System.Security.Cryptography;
using System.Text;
using Ghos.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ghos.Web.WinterWatch;

public sealed record WinterWatchCredentials(
    string FunctionUrl,
    string IntegrationSecret,
    string InviteRedirectUrl);

public sealed class WinterWatchConnectionException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);

public sealed class WinterWatchCredentialStore
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IDataProtector _protector;
    private readonly WinterWatchAdminOptions _options;

    public WinterWatchCredentialStore(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<WinterWatchAdminOptions> options)
    {
        _dbContextFactory = dbContextFactory;
        _protector = dataProtectionProvider.CreateProtector(
            "GreenHills.GHOS.WinterWatch.AdminIntegration.v1");
        _options = options.Value;
    }

    public bool UsesServerManagedCredentials =>
        !string.IsNullOrWhiteSpace(_options.FunctionUrl) &&
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
        return await dbContext.WinterWatchConnectionSettings
            .AnyAsync(cancellationToken);
    }

    public async Task<WinterWatchCredentials?> GetAsync(
        CancellationToken cancellationToken = default)
    {
        if (UsesServerManagedCredentials)
        {
            return new WinterWatchCredentials(
                NormalizeFunctionUrl(_options.FunctionUrl),
                _options.IntegrationSecret.Trim(),
                NormalizeRedirectUrl(_options.InviteRedirectUrl));
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await dbContext.WinterWatchConnectionSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            return null;
        }

        try
        {
            return new WinterWatchCredentials(
                settings.FunctionUrl,
                _protector.Unprotect(settings.EncryptedIntegrationSecret),
                settings.InviteRedirectUrl);
        }
        catch (Exception exception)
        {
            throw new WinterWatchConnectionException(
                "GHOS could not decrypt the saved WinterWatch secret. Re-enter it in WinterWatch administration.",
                exception);
        }
    }

    public async Task SaveAsync(
        string functionUrl,
        string integrationSecret,
        string inviteRedirectUrl,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var normalizedFunctionUrl = NormalizeFunctionUrl(functionUrl);
        var normalizedRedirectUrl = NormalizeRedirectUrl(inviteRedirectUrl);
        if (string.IsNullOrWhiteSpace(integrationSecret))
        {
            throw new WinterWatchConnectionException(
                "Enter the WinterWatch integration secret.");
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await dbContext.WinterWatchConnectionSettings
            .SingleOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new WinterWatchConnectionSettings();
            dbContext.WinterWatchConnectionSettings.Add(settings);
        }

        settings.FunctionUrl = normalizedFunctionUrl;
        settings.EncryptedIntegrationSecret =
            _protector.Protect(integrationSecret.Trim());
        settings.InviteRedirectUrl = normalizedRedirectUrl;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedByUserId = userId;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static string NormalizeFunctionUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new WinterWatchConnectionException(
                "Enter the secure HTTPS URL for the GHOS WinterWatch admin function.");
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public static string NormalizeRedirectUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new WinterWatchConnectionException(
                "Enter the secure WinterWatch invitation callback URL.");
        }

        return uri.AbsoluteUri;
    }

    public static string CreateFingerprint(string integrationSecret)
    {
        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(integrationSecret.Trim()));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
