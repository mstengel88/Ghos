namespace Ghos.Web.Data;

public sealed class AssetProductLink
{
    public Guid DigitalAssetId { get; set; }

    public DigitalAsset DigitalAsset { get; set; } = null!;

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public int SortOrder { get; set; }
}
