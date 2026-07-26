using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductAlternateName> ProductAlternateNames => Set<ProductAlternateName>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ProductCategory>(category =>
        {
            category.HasIndex(item => item.Name).IsUnique();
            category.HasIndex(item => item.Slug).IsUnique();
        });

        builder.Entity<Product>(product =>
        {
            product.HasIndex(item => item.Name).IsUnique();
            product.HasIndex(item => item.Slug).IsUnique();
            product.HasIndex(item => item.ProductCode)
                .IsUnique()
                .HasFilter("\"ProductCode\" IS NOT NULL");
            product.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);

            product.HasOne(item => item.ProductCategory)
                .WithMany(category => category.Products)
                .HasForeignKey(item => item.ProductCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductAlternateName>(alternateName =>
        {
            alternateName.HasIndex(item => item.NormalizedName).IsUnique();

            alternateName.HasOne(item => item.Product)
                .WithMany(product => product.AlternateNames)
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
