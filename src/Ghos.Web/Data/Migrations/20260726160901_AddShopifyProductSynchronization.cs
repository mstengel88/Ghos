using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShopifyProductSynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Name",
                table: "Products");

            migrationBuilder.AddColumn<DateTime>(
                name: "ShopifyCreatedAtUtc",
                table: "Products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifyDescriptionHtml",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifyFeaturedImageAlt",
                table: "Products",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifyFeaturedImageUrl",
                table: "Products",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifyHandle",
                table: "Products",
                type: "character varying(180)",
                maxLength: 180,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShopifyLastSyncedAtUtc",
                table: "Products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifyProductId",
                table: "Products",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifyProductType",
                table: "Products",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShopifyPublishedAtUtc",
                table: "Products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifySeoDescription",
                table: "Products",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifySeoTitle",
                table: "Products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifyStatus",
                table: "Products",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifyTags",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifyTitle",
                table: "Products",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShopifyUpdatedAtUtc",
                table: "Products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifyVendor",
                table: "Products",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopifyVariantId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CompareAtPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    AvailableForSale = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariants_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShopifyConnectionSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EncryptedClientId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EncryptedClientSecret = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopifyConnectionSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShopifySyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ShopifyProductCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedCount = table.Column<int>(type: "integer", nullable: false),
                    UpdatedCount = table.Column<int>(type: "integer", nullable: false),
                    UnchangedCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    InitiatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopifySyncRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Products_ShopifyHandle",
                table: "Products",
                column: "ShopifyHandle",
                filter: "\"ShopifyHandle\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_ShopifyProductId",
                table: "Products",
                column: "ShopifyProductId",
                unique: true,
                filter: "\"ShopifyProductId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId",
                table: "ProductVariants",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ShopifyVariantId",
                table: "ProductVariants",
                column: "ShopifyVariantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShopifySyncRuns_StartedAtUtc",
                table: "ShopifySyncRuns",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductVariants");

            migrationBuilder.DropTable(
                name: "ShopifyConnectionSettings");

            migrationBuilder.DropTable(
                name: "ShopifySyncRuns");

            migrationBuilder.DropIndex(
                name: "IX_Products_Name",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_ShopifyHandle",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_ShopifyProductId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifyCreatedAtUtc",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifyDescriptionHtml",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifyFeaturedImageAlt",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifyFeaturedImageUrl",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifyHandle",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifyLastSyncedAtUtc",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifyProductId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifyProductType",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifyPublishedAtUtc",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifySeoDescription",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifySeoTitle",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifyStatus",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifyTags",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifyTitle",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifyUpdatedAtUtc",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShopifyVendor",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name",
                unique: true);
        }
    }
}
