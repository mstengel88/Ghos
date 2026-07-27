using System.Text.Json;

namespace Ghos.Web.DumpSite;

public sealed class DumpSiteOperatorService(
    DumpSiteCredentialStore credentialStore,
    DumpSiteBridgeClient bridgeClient)
{
    public Task<bool> IsConfiguredAsync(
        CancellationToken cancellationToken = default) =>
        credentialStore.HasCredentialsAsync(cancellationToken);

    public async Task<IReadOnlyList<DumpSiteQueueRecord>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration =
            await RequireConfigurationAsync(cancellationToken);
        var entries = await bridgeClient.ListAsync(
            configuration.Credentials,
            cancellationToken: cancellationToken);
        return entries
            .Select(entry => Map(entry, configuration.Settings))
            .ToList();
    }

    public async Task<DumpSiteQueueRecord> ClaimAsync(
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        var configuration =
            await RequireConfigurationAsync(cancellationToken);
        var entry = await bridgeClient.ClaimAsync(
            configuration.Credentials,
            entryId,
            cancellationToken);
        return Map(entry, configuration.Settings);
    }

    public async Task ReleaseAsync(
        Guid entryId,
        Guid claimToken,
        CancellationToken cancellationToken = default)
    {
        var configuration =
            await RequireConfigurationAsync(cancellationToken);
        await bridgeClient.ReleaseAsync(
            configuration.Credentials,
            entryId,
            claimToken,
            cancellationToken);
    }

    public async Task CompleteAsync(
        Guid entryId,
        Guid claimToken,
        string rawTicketNumber,
        CancellationToken cancellationToken = default)
    {
        var ticketNumber = CleanTicketNumber(rawTicketNumber);
        var configuration =
            await RequireConfigurationAsync(cancellationToken);
        await bridgeClient.CompleteAsync(
            configuration.Credentials,
            entryId,
            claimToken,
            ticketNumber,
            cancellationToken);
    }

    public static string CleanTicketNumber(string value)
    {
        var ticketNumber = value.Trim();
        if (string.IsNullOrWhiteSpace(ticketNumber))
        {
            throw new DumpSiteConnectionException(
                "Enter the hold-ticket number returned by Counterpoint.");
        }

        if (ticketNumber.Length > 100 ||
            ticketNumber.Any(character =>
                !(char.IsLetterOrDigit(character) ||
                  character is '.' or '_' or '/' or '-')))
        {
            throw new DumpSiteConnectionException(
                "The Counterpoint ticket number contains unexpected characters.");
        }

        return ticketNumber;
    }

    private async Task<DumpSiteConfiguration> RequireConfigurationAsync(
        CancellationToken cancellationToken)
    {
        return await credentialStore.GetAsync(cancellationToken) ??
            throw new DumpSiteConnectionException(
                "Configure the Dumpsite connection in Settings before opening the queue.");
    }

    private static DumpSiteQueueRecord Map(
        DumpSiteBridgeEntry entry,
        Data.DumpSiteConnectionSettings settings)
    {
        try
        {
            using var itemDocument =
                JsonDocument.Parse(settings.ItemMappingsJson);
            using var companyDocument =
                JsonDocument.Parse(settings.CompanyMappingsJson);

            var itemKey =
                $"{entry.MaterialType}|{entry.VehicleType}";
            if (!itemDocument.RootElement.TryGetProperty(
                    itemKey,
                    out var item))
            {
                throw new DumpSiteConnectionException(
                    $"No item mapping exists for {itemKey}.");
            }

            var company = FindCompany(
                companyDocument.RootElement,
                entry.ShopifyCompanyId,
                entry.CompanyName);
            var customerNumber = GetString(
                company,
                "counterpointCustomerNumber");
            if (string.IsNullOrWhiteSpace(customerNumber))
            {
                throw new DumpSiteConnectionException(
                    $"No Counterpoint customer number is mapped for {entry.CompanyName}.");
            }

            if (entry.ConfirmationId.Length <= 5 ||
                !entry.ConfirmationId.StartsWith(
                    "201-D",
                    StringComparison.Ordinal) ||
                !entry.ConfirmationId[5..].All(char.IsDigit))
            {
                throw new DumpSiteConnectionException(
                    $"Unexpected dump-site confirmation number: {entry.ConfirmationId}.");
            }

            var unitPrice = GetDecimal(item, "price");
            var tax = GetDecimal(item, "tax");
            var barcode = GetString(item, "sku");
            if (string.IsNullOrWhiteSpace(barcode))
            {
                throw new DumpSiteConnectionException(
                    $"The item mapping for {itemKey} does not have a barcode.");
            }
            var itemDescription = FirstValue(
                GetString(item, "name"),
                $"{entry.MaterialType} - {entry.VehicleType}");
            var submittedByName = GetCustomerField(
                entry.ShopifyCustomer,
                "name");
            var submittedByEmail = GetCustomerField(
                entry.ShopifyCustomer,
                "email");

            return new DumpSiteQueueRecord(
                entry.Id,
                entry.ClaimToken,
                entry.ClaimedByThisOperator ||
                    entry.ClaimToken.HasValue,
                entry.ConfirmationId,
                entry.SubmittedAtUtc,
                entry.CompanyName,
                entry.ShopifyCompanyId,
                customerNumber,
                submittedByName,
                submittedByEmail,
                entry.TruckNumber,
                entry.DriverName,
                entry.MaterialType,
                entry.VehicleType,
                barcode,
                itemDescription,
                1,
                unitPrice,
                tax,
                unitPrice + tax,
                FirstValue(
                    GetString(company, "location"),
                    settings.CounterpointLocation),
                FirstValue(
                    GetString(company, "station"),
                    settings.CounterpointStation),
                FirstValue(
                    GetString(company, "drawer"),
                    settings.CounterpointDrawer),
                FirstValue(
                    GetString(company, "salesRep"),
                    settings.CounterpointSalesRep),
                [
                    $"Dump site {entry.ConfirmationId}",
                    $"Company: {entry.CompanyName}",
                    $"Truck: {entry.TruckNumber}",
                    $"Driver: {entry.DriverName}",
                    $"{entry.MaterialType} | {entry.VehicleType}"
                ],
                null);
        }
        catch (Exception exception)
        {
            return new DumpSiteQueueRecord(
                entry.Id,
                entry.ClaimToken,
                entry.ClaimedByThisOperator ||
                    entry.ClaimToken.HasValue,
                entry.ConfirmationId,
                entry.SubmittedAtUtc,
                entry.CompanyName,
                entry.ShopifyCompanyId,
                string.Empty,
                string.Empty,
                string.Empty,
                entry.TruckNumber,
                entry.DriverName,
                entry.MaterialType,
                entry.VehicleType,
                string.Empty,
                string.Empty,
                1,
                0,
                0,
                0,
                settings.CounterpointLocation,
                settings.CounterpointStation,
                settings.CounterpointDrawer,
                settings.CounterpointSalesRep,
                [],
                exception.Message);
        }
    }

    private static JsonElement FindCompany(
        JsonElement companies,
        string companyId,
        string companyName)
    {
        if (!string.IsNullOrWhiteSpace(companyId) &&
            companies.TryGetProperty(companyId, out var byId))
        {
            return byId;
        }

        if (!string.IsNullOrWhiteSpace(companyName) &&
            companies.TryGetProperty(companyName, out var byName))
        {
            return byName;
        }

        return default;
    }

    private static string GetString(
        JsonElement element,
        string property)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var value))
        {
            return value.GetString()?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    private static decimal GetDecimal(
        JsonElement element,
        string property)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var value) &&
            value.TryGetDecimal(out var result) &&
            result >= 0)
        {
            return decimal.Round(result, 2);
        }

        throw new DumpSiteConnectionException(
            $"The {property} mapping is missing or invalid.");
    }

    private static string GetCustomerField(
        JsonElement? customer,
        string property)
    {
        if (customer is { ValueKind: JsonValueKind.Object } value &&
            value.TryGetProperty(property, out var field))
        {
            return field.GetString()?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string FirstValue(
        string value,
        string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
