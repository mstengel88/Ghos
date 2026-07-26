using Ghos.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ghos.Web.Shopify;

public sealed record ShopifyCredentials(string ClientId, string ClientSecret);

public sealed class ShopifyCredentialStore
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IDataProtector _protector;
    private readonly ShopifyOptions _options;

    public ShopifyCredentialStore(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<ShopifyOptions> options)
    {
        _dbContextFactory = dbContextFactory;
        _protector = dataProtectionProvider.CreateProtector(
            "GreenHills.GHOS.Shopify.ClientCredentials.v1");
        _options = options.Value;
    }

    public async Task<bool> HasCredentialsAsync(CancellationToken cancellationToken = default)
    {
        if (_options.HasEnvironmentCredentials)
        {
            return true;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.ShopifyConnectionSettings.AnyAsync(cancellationToken);
    }

    public async Task<ShopifyCredentials?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_options.HasEnvironmentCredentials)
        {
            return new ShopifyCredentials(
                _options.ClientId!.Trim(),
                _options.ClientSecret!.Trim());
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await dbContext.ShopifyConnectionSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            return null;
        }

        try
        {
            return new ShopifyCredentials(
                _protector.Unprotect(settings.EncryptedClientId),
                _protector.Unprotect(settings.EncryptedClientSecret));
        }
        catch (Exception exception)
        {
            throw new ShopifyConnectionException(
                "GHOS could not decrypt the saved Shopify credentials. Re-enter them in the Shopify setup screen.",
                exception);
        }
    }

    public async Task SaveAsync(
        string clientId,
        string clientSecret,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await dbContext.ShopifyConnectionSettings
            .SingleOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new ShopifyConnectionSettings();
            dbContext.ShopifyConnectionSettings.Add(settings);
        }

        settings.EncryptedClientId = _protector.Protect(clientId.Trim());
        settings.EncryptedClientSecret = _protector.Protect(clientSecret.Trim());
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedByUserId = userId;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
