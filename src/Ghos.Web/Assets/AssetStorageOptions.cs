namespace Ghos.Web.Assets;

public sealed class AssetStorageOptions
{
    public const string SectionName = "AssetStorage";

    public string RootPath { get; set; } = "/var/lib/ghos/assets";

    public long MaxFileSizeBytes { get; set; } = 1_073_741_824;
}
