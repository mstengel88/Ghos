namespace Ghos.Web.DumpSite;

public static class DumpSiteMaintenanceCommand
{
    private const string ExportArgument =
        "--maintenance-export-dumpsite-secret=";
    private const string ActivateArgument =
        "--maintenance-activate-local-dumpsite";
    private const string AllowedDirectory = "/tmp/ghos-maintenance";

    public static async Task<bool> TryExecuteAsync(
        string[] arguments,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var exportArgument = arguments.SingleOrDefault(value =>
            value.StartsWith(ExportArgument, StringComparison.Ordinal));
        var activate = arguments.Contains(
            ActivateArgument,
            StringComparer.Ordinal);
        if (exportArgument is null && !activate)
        {
            return false;
        }

        await using var scope = services.CreateAsyncScope();
        var credentialStore =
            scope.ServiceProvider.GetRequiredService<DumpSiteCredentialStore>();
        var configuration = await credentialStore.GetAsync(cancellationToken) ??
            throw new InvalidOperationException(
                "The Dumpsite connection is not configured in GHOS.");

        if (exportArgument is not null)
        {
            var destination = ValidateDestination(
                exportArgument[ExportArgument.Length..]);
            await ExportSecretAsync(
                destination,
                configuration.Credentials.SharedSecret,
                cancellationToken);
            Console.WriteLine(
                $"Dumpsite credential exported to protected maintenance file '{destination}'.");
        }

        if (activate)
        {
            var bridgeClient =
                scope.ServiceProvider.GetRequiredService<DumpSiteBridgeClient>();
            await bridgeClient.TestAsync(
                DumpSiteCredentialStore.LocalOperationsBridgeUrl,
                configuration.Credentials.SharedSecret,
                configuration.Credentials.BridgeId,
                cancellationToken);
            await credentialStore.UpdateBridgeEndpointAsync(
                DumpSiteCredentialStore.LocalOperationsBridgeUrl,
                userId: null,
                cancellationToken);
            Console.WriteLine(
                "Dumpsite local Operations bridge verified and activated.");
        }

        return true;
    }

    private static async Task ExportSecretAsync(
        string destination,
        string sharedSecret,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "The Dumpsite maintenance export is only supported by the Linux GHOS container.");
        }

        Directory.CreateDirectory(AllowedDirectory);
        await using (var stream = new FileStream(
            destination,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough,
                UnixCreateMode =
                    UnixFileMode.UserRead | UnixFileMode.UserWrite
            }))
        await using (var writer = new StreamWriter(stream))
        {
            await writer.WriteAsync(
                sharedSecret.AsMemory(),
                cancellationToken);
        }
    }

    internal static string ValidateDestination(string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            throw new InvalidOperationException(
                "A maintenance export destination is required.");
        }

        var fullPath = Path.GetFullPath(requestedPath);
        var allowedPrefix = AllowedDirectory + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(allowedPrefix, StringComparison.Ordinal) ||
            string.Equals(fullPath, AllowedDirectory, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The maintenance export destination must be beneath '{AllowedDirectory}'.");
        }

        return fullPath;
    }
}
