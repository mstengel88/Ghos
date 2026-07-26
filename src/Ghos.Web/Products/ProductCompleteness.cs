using Ghos.Web.Data;

namespace Ghos.Web.Products;

public static class ProductCompleteness
{
    public static int Calculate(Product product)
    {
        var checks = new[]
        {
            !string.IsNullOrWhiteSpace(product.Name),
            product.ProductCategoryId != Guid.Empty,
            !string.IsNullOrWhiteSpace(product.ProductCode),
            !string.IsNullOrWhiteSpace(product.ShortDescription),
            !string.IsNullOrWhiteSpace(product.Description),
            !string.IsNullOrWhiteSpace(product.BestUses),
            !string.IsNullOrWhiteSpace(product.Limitations),
            product.AlternateNames.Count > 0,
            product.AvailableForPickup || product.AvailableForDelivery,
            product.Status == ProductStatus.Active
        };

        return (int)Math.Round(checks.Count(check => check) * 100d / checks.Length);
    }
}
