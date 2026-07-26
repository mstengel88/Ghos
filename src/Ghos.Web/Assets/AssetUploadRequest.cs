using Ghos.Web.Data;

namespace Ghos.Web.Assets;

public sealed record AssetUploadRequest(
    Stream Content,
    string OriginalFileName,
    long FileSizeBytes,
    string? Title,
    string? Description,
    string? Tags,
    int Rating,
    Guid? ProductId,
    string? UserId);

public sealed record AssetUploadResult(
    DigitalAsset Asset,
    bool Created);
