using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class ShopifyCollection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(80)]
    public string ShopifyCollectionId { get; set; } = string.Empty;

    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(180)]
    public string Handle { get; set; } = string.Empty;

    public DateTime LastSyncedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ProductShopifyCollection> ProductLinks { get; set; } = [];
}
