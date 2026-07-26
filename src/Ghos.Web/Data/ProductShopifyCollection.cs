namespace Ghos.Web.Data;

public sealed class ProductShopifyCollection
{
    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public Guid ShopifyCollectionId { get; set; }

    public ShopifyCollection ShopifyCollection { get; set; } = null!;
}
