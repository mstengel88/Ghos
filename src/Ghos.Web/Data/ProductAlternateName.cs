using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class ProductAlternateName
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(160)]
    public string NormalizedName { get; set; } = string.Empty;
}
