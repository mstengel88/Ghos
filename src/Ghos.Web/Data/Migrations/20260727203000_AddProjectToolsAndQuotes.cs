using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260727203000_AddProjectToolsAndQuotes")]
public partial class AddProjectToolsAndQuotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CustomerQuotes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                QuoteNumber = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false),
                Status = table.Column<string>(
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: false),
                CustomerName = table.Column<string>(
                    type: "character varying(160)",
                    maxLength: 160,
                    nullable: false),
                CompanyName = table.Column<string>(
                    type: "character varying(160)",
                    maxLength: 160,
                    nullable: true),
                Email = table.Column<string>(
                    type: "character varying(240)",
                    maxLength: 240,
                    nullable: true),
                Phone = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: true),
                AddressLine1 = table.Column<string>(
                    type: "character varying(240)",
                    maxLength: 240,
                    nullable: true),
                City = table.Column<string>(
                    type: "character varying(120)",
                    maxLength: 120,
                    nullable: true),
                State = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: true),
                PostalCode = table.Column<string>(
                    type: "character varying(20)",
                    maxLength: 20,
                    nullable: true),
                IsContractor = table.Column<bool>(
                    type: "boolean",
                    nullable: false),
                IsTaxExempt = table.Column<bool>(
                    type: "boolean",
                    nullable: false),
                Subtotal = table.Column<decimal>(
                    type: "numeric(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false),
                DeliveryAmount = table.Column<decimal>(
                    type: "numeric(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false),
                TaxRate = table.Column<decimal>(
                    type: "numeric(8,6)",
                    precision: 8,
                    scale: 6,
                    nullable: false),
                TaxAmount = table.Column<decimal>(
                    type: "numeric(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false),
                Total = table.Column<decimal>(
                    type: "numeric(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false),
                InternalNotes = table.Column<string>(
                    type: "text",
                    nullable: true),
                CustomerNotes = table.Column<string>(
                    type: "text",
                    nullable: true),
                ValidUntilUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true),
                CreatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false),
                CreatedByUserId = table.Column<string>(
                    type: "character varying(450)",
                    maxLength: 450,
                    nullable: true),
                UpdatedByUserId = table.Column<string>(
                    type: "character varying(450)",
                    maxLength: 450,
                    nullable: true),
                LegacyExternalId = table.Column<string>(
                    type: "character varying(120)",
                    maxLength: 120,
                    nullable: true),
                ShopifyDraftOrderId = table.Column<string>(
                    type: "character varying(120)",
                    maxLength: 120,
                    nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerQuotes", item => item.Id);
            });

        migrationBuilder.CreateTable(
            name: "ProductMaterialProfiles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                SoldBy = table.Column<string>(
                    type: "character varying(24)",
                    maxLength: 24,
                    nullable: false),
                TonsPerCubicYard = table.Column<decimal>(
                    type: "numeric(10,4)",
                    precision: 10,
                    scale: 4,
                    nullable: true),
                OrderIncrement = table.Column<decimal>(
                    type: "numeric(10,2)",
                    precision: 10,
                    scale: 2,
                    nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false),
                UpdatedByUserId = table.Column<string>(
                    type: "character varying(450)",
                    maxLength: 450,
                    nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_ProductMaterialProfiles",
                    item => item.Id);
                table.ForeignKey(
                    name: "FK_ProductMaterialProfiles_Products_ProductId",
                    column: item => item.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "CustomerQuoteLines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerQuoteId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                ProductId = table.Column<Guid>(
                    type: "uuid",
                    nullable: true),
                ProductVariantId = table.Column<Guid>(
                    type: "uuid",
                    nullable: true),
                Description = table.Column<string>(
                    type: "character varying(180)",
                    maxLength: 180,
                    nullable: false),
                Sku = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: true),
                UnitLabel = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false),
                Quantity = table.Column<decimal>(
                    type: "numeric(18,3)",
                    precision: 18,
                    scale: 3,
                    nullable: false),
                UnitPrice = table.Column<decimal>(
                    type: "numeric(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false),
                LineTotal = table.Column<decimal>(
                    type: "numeric(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false),
                SortOrder = table.Column<int>(
                    type: "integer",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerQuoteLines", item => item.Id);
                table.ForeignKey(
                    name: "FK_CustomerQuoteLines_CustomerQuotes_CustomerQuoteId",
                    column: item => item.CustomerQuoteId,
                    principalTable: "CustomerQuotes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_CustomerQuoteLines_Products_ProductId",
                    column: item => item.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_CustomerQuoteLines_ProductVariants_ProductVariantId",
                    column: item => item.ProductVariantId,
                    principalTable: "ProductVariants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CustomerQuoteLines_CustomerQuoteId_SortOrder",
            table: "CustomerQuoteLines",
            columns: new[] { "CustomerQuoteId", "SortOrder" });
        migrationBuilder.CreateIndex(
            name: "IX_CustomerQuoteLines_ProductId",
            table: "CustomerQuoteLines",
            column: "ProductId");
        migrationBuilder.CreateIndex(
            name: "IX_CustomerQuoteLines_ProductVariantId",
            table: "CustomerQuoteLines",
            column: "ProductVariantId");
        migrationBuilder.CreateIndex(
            name: "IX_CustomerQuotes_QuoteNumber",
            table: "CustomerQuotes",
            column: "QuoteNumber",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_CustomerQuotes_Status_UpdatedAtUtc",
            table: "CustomerQuotes",
            columns: new[] { "Status", "UpdatedAtUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_ProductMaterialProfiles_ProductId",
            table: "ProductMaterialProfiles",
            column: "ProductId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CustomerQuoteLines");
        migrationBuilder.DropTable(name: "ProductMaterialProfiles");
        migrationBuilder.DropTable(name: "CustomerQuotes");
    }
}
