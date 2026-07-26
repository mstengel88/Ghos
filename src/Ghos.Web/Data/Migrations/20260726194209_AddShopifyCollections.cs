using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260726194209_AddShopifyCollections")]
public partial class AddShopifyCollections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ShopifyCollections",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ShopifyCollectionId = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false),
                Title = table.Column<string>(
                    type: "character varying(160)",
                    maxLength: 160,
                    nullable: false),
                Handle = table.Column<string>(
                    type: "character varying(180)",
                    maxLength: 180,
                    nullable: false),
                LastSyncedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShopifyCollections", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ProductShopifyCollections",
            columns: table => new
            {
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                ShopifyCollectionId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_ProductShopifyCollections",
                    x => new { x.ProductId, x.ShopifyCollectionId });
                table.ForeignKey(
                    name: "FK_ProductShopifyCollections_Products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ProductShopifyCollections_ShopifyCollections_ShopifyCollectionId",
                    column: x => x.ShopifyCollectionId,
                    principalTable: "ShopifyCollections",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProductShopifyCollections_ShopifyCollectionId",
            table: "ProductShopifyCollections",
            column: "ShopifyCollectionId");

        migrationBuilder.CreateIndex(
            name: "IX_ShopifyCollections_Handle",
            table: "ShopifyCollections",
            column: "Handle",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ShopifyCollections_ShopifyCollectionId",
            table: "ShopifyCollections",
            column: "ShopifyCollectionId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ShopifyCollections_Title",
            table: "ShopifyCollections",
            column: "Title");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProductShopifyCollections");
        migrationBuilder.DropTable(name: "ShopifyCollections");
    }
}
