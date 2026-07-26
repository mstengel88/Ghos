using System.Security.Cryptography;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ghos.Web.Assets;

public sealed class AssetStorageService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IOptions<AssetStorageOptions> options)
{
    private static readonly IReadOnlyDictionary<string, (AssetKind Kind, string ContentType)>
        AllowedExtensions = new Dictionary<string, (AssetKind, string)>(
            StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = (AssetKind.Image, "image/jpeg"),
            [".jpeg"] = (AssetKind.Image, "image/jpeg"),
            [".png"] = (AssetKind.Image, "image/png"),
            [".webp"] = (AssetKind.Image, "image/webp"),
            [".gif"] = (AssetKind.Image, "image/gif"),
            [".heic"] = (AssetKind.Image, "image/heic"),
            [".heif"] = (AssetKind.Image, "image/heif"),
            [".mp4"] = (AssetKind.Video, "video/mp4"),
            [".mov"] = (AssetKind.Video, "video/quicktime"),
            [".m4v"] = (AssetKind.Video, "video/x-m4v"),
            [".webm"] = (AssetKind.Video, "video/webm"),
            [".pdf"] = (AssetKind.Document, "application/pdf"),
            [".docx"] = (
                AssetKind.Document,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            [".xlsx"] = (
                AssetKind.Document,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            [".pptx"] = (
                AssetKind.Document,
                "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
            [".csv"] = (AssetKind.Document, "text/csv"),
            [".txt"] = (AssetKind.Document, "text/plain")
        };

    private readonly AssetStorageOptions storageOptions = options.Value;

    public long MaxFileSizeBytes => storageOptions.MaxFileSizeBytes;

    public async Task<AssetUploadResult> StoreAsync(
        AssetUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.FileSizeBytes <= 0)
        {
            throw new AssetStorageException("Choose a non-empty file.");
        }

        if (request.FileSizeBytes > storageOptions.MaxFileSizeBytes)
        {
            throw new AssetStorageException(
                $"The file is larger than the {FormatBytes(storageOptions.MaxFileSizeBytes)} upload limit.");
        }

        var originalFileName = Path.GetFileName(request.OriginalFileName).Trim();
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var sourceUrl = NullIfWhiteSpace(request.SourceUrl);

        if (sourceUrl?.Length > 2048)
        {
            throw new AssetStorageException("The original source URL is too long.");
        }

        if (string.IsNullOrWhiteSpace(originalFileName) ||
            !AllowedExtensions.TryGetValue(extension, out var fileType))
        {
            throw new AssetStorageException(
                "This file type is not supported. Use a common image, video, PDF, Office document, CSV, or text file.");
        }

        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (request.ProductId is not null &&
            !await dbContext.Products.AnyAsync(
                product => product.Id == request.ProductId,
                cancellationToken))
        {
            throw new AssetStorageException("The selected product no longer exists.");
        }

        var rootPath = Path.GetFullPath(storageOptions.RootPath);
        var incomingPath = Path.Combine(rootPath, "incoming");
        Directory.CreateDirectory(incomingPath);
        var temporaryPath = Path.Combine(
            incomingPath,
            $"{Guid.NewGuid():N}.uploading");

        try
        {
            var storedFile = await CopyAndHashAsync(
                request.Content,
                temporaryPath,
                storageOptions.MaxFileSizeBytes,
                cancellationToken);

            if (storedFile.BytesWritten == 0)
            {
                throw new AssetStorageException("Choose a non-empty file.");
            }

            var existing = await dbContext.DigitalAssets
                .Include(asset => asset.ProductLinks)
                .SingleOrDefaultAsync(
                    asset => asset.Sha256Hash == storedFile.Sha256Hash,
                    cancellationToken);

            if (existing is not null)
            {
                var existingChanged = false;

                if (request.ProductId is not null &&
                    existing.ProductLinks.All(link =>
                        link.ProductId != request.ProductId))
                {
                    existing.ProductLinks.Add(new AssetProductLink
                    {
                        ProductId = request.ProductId.Value
                    });
                    existingChanged = true;
                }

                if (sourceUrl is not null && existing.SourceUrl is null)
                {
                    existing.Source = request.Source;
                    existing.SourceUrl = sourceUrl;
                    existingChanged = true;
                }

                if (existingChanged)
                {
                    existing.UpdatedAtUtc = DateTime.UtcNow;
                    existing.UpdatedByUserId = request.UserId;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                return new AssetUploadResult(existing, Created: false);
            }

            var now = DateTime.UtcNow;
            var asset = new DigitalAsset
            {
                Title = NormalizeTitle(request.Title, originalFileName),
                OriginalFileName = originalFileName,
                ContentType = fileType.ContentType,
                Kind = fileType.Kind,
                Status = AssetStatus.PendingReview,
                Source = request.Source,
                SourceUrl = sourceUrl,
                FileSizeBytes = storedFile.BytesWritten,
                Sha256Hash = storedFile.Sha256Hash,
                Description = NullIfWhiteSpace(request.Description),
                Tags = NormalizeTags(request.Tags),
                Rating = Math.Clamp(request.Rating, 0, 5),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedByUserId = request.UserId,
                UpdatedByUserId = request.UserId
            };

            var relativeDirectory = Path.Combine(
                "originals",
                now.ToString("yyyy"),
                now.ToString("MM"));
            var relativePath = Path.Combine(
                relativeDirectory,
                $"{asset.Id:N}{extension}");
            var finalPath = GetAbsolutePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            File.Move(temporaryPath, finalPath);
            asset.RelativePath = relativePath.Replace('\\', '/');

            if (request.ProductId is not null)
            {
                asset.ProductLinks.Add(new AssetProductLink
                {
                    ProductId = request.ProductId.Value
                });
            }

            dbContext.DigitalAssets.Add(asset);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                File.Delete(finalPath);
                throw;
            }

            return new AssetUploadResult(asset, Created: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public string GetAbsolutePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new AssetStorageException("The stored asset path is invalid.");
        }

        var rootPath = Path.GetFullPath(storageOptions.RootPath);
        var rootPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        var absolutePath = Path.GetFullPath(Path.Combine(rootPath, relativePath));

        if (!absolutePath.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new AssetStorageException("The stored asset path is invalid.");
        }

        return absolutePath;
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    private static async Task<(string Sha256Hash, long BytesWritten)> CopyAndHashAsync(
        Stream source,
        string destinationPath,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 1024,
            useAsync: true);
        var buffer = new byte[1024 * 1024];
        long totalBytes = 0;

        while (true)
        {
            var bytesRead = await source.ReadAsync(
                buffer.AsMemory(),
                cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;

            if (totalBytes > maximumBytes)
            {
                throw new AssetStorageException(
                    $"The file is larger than the {FormatBytes(maximumBytes)} upload limit.");
            }

            hash.AppendData(buffer, 0, bytesRead);
            await destination.WriteAsync(
                buffer.AsMemory(0, bytesRead),
                cancellationToken);
        }

        return (Convert.ToHexString(hash.GetHashAndReset()), totalBytes);
    }

    private static string NormalizeTitle(string? title, string originalFileName)
    {
        var value = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(originalFileName)
            : title.Trim();

        return value.Length <= 160 ? value : value[..160];
    }

    private static string? NormalizeTags(string? tags)
    {
        var normalized = (tags ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase);
        var value = string.Join(", ", normalized);

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= 2000
                ? value
                : value[..2000];
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
