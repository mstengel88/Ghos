using Ghos.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ghos.Web.Shopify;

public sealed class ShopifyDraftOrderCredentialStore
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IDataProtector _protector;
    private readonly ShopifyOptions _options;

    public ShopifyDraftOrderCredentialStore(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<ShopifyOptions> options)
    {
        _dbContextFactory = dbContextFactory;
        _protector = dataProtectionProvider.CreateProtector(
            "GreenHills.GHOS.Shopify.DraftOrderClientCredentials.v1");
        _options = options.Value;
    }

    public async Task<bool> HasCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_options.HasDraftOrderEnvironmentCredentials)
        {
            return true;
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.ShopifyConnectionSettings.AnyAsync(
            settings =>
                settings.EncryptedDraftOrderClientId != null &&
                settings.EncryptedDraftOrderClientSecret != null,
            cancellationToken);
    }

    public async Task<ShopifyCredentials?> GetAsync(
        CancellationToken cancellationToken = default)
    {
        if (_options.HasDraftOrderEnvironmentCredentials)
        {
            return new ShopifyCredentials(
                _options.DraftOrderClientId!.Trim(),
                _options.DraftOrderClientSecret!.Trim());
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await dbContext.ShopifyConnectionSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (settings?.EncryptedDraftOrderClientId is null ||
            settings.EncryptedDraftOrderClientSecret is null)
        {
            return null;
        }

        try
        {
            return new ShopifyCredentials(
                _protector.Unprotect(
                    settings.EncryptedDraftOrderClientId),
                _protector.Unprotect(
                    settings.EncryptedDraftOrderClientSecret));
        }
        catch (Exception exception)
        {
            throw new ShopifyConnectionException(
                "GHOS could not decrypt the saved Shopify draft-order credentials. Re-enter them in the Shopify setup screen.",
                exception);
        }
    }

    public async Task SaveAsync(
        string clientId,
        string clientSecret,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await dbContext.ShopifyConnectionSettings
            .SingleOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new ShopifyConnectionSettings();
            dbContext.ShopifyConnectionSettings.Add(settings);
        }

        settings.EncryptedDraftOrderClientId =
            _protector.Protect(clientId.Trim());
        settings.EncryptedDraftOrderClientSecret =
            _protector.Protect(clientSecret.Trim());
        settings.DraftOrderUpdatedAtUtc = DateTime.UtcNow;
        settings.DraftOrderUpdatedByUserId = userId;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
