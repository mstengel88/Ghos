using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchQuoteParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ContractorTier1Price",
                table: "ProductVariants",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ContractorTier2Price",
                table: "ProductVariants",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "ProductVariants",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitLabel",
                table: "ProductVariants",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "CustomerQuotes",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Audience",
                table: "CustomerQuotes",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Customer");

            migrationBuilder.AddColumn<string>(
                name: "BillingAddressLine1",
                table: "CustomerQuotes",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingAddressLine2",
                table: "CustomerQuotes",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingCity",
                table: "CustomerQuotes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingCountry",
                table: "CustomerQuotes",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingPostalCode",
                table: "CustomerQuotes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingState",
                table: "CustomerQuotes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CalculatedDeliveryAmount",
                table: "CustomerQuotes",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractorTier",
                table: "CustomerQuotes",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Tier1");

            migrationBuilder.AddColumn<decimal>(
                name: "CustomDeliveryAmount",
                table: "CustomerQuotes",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryDescription",
                table: "CustomerQuotes",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryEta",
                table: "CustomerQuotes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryServiceName",
                table: "CustomerQuotes",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliverySummary",
                table: "CustomerQuotes",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentTermsDueInDays",
                table: "CustomerQuotes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTermsName",
                table: "CustomerQuotes",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTermsTemplateId",
                table: "CustomerQuotes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RatePerMinute",
                table: "CustomerQuotes",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingQuantity",
                table: "CustomerQuotes",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingRate",
                table: "CustomerQuotes",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingUnit",
                table: "CustomerQuotes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifyCompanyContactId",
                table: "CustomerQuotes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifyCompanyId",
                table: "CustomerQuotes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifyCompanyLocationId",
                table: "CustomerQuotes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifyDraftOrderUrl",
                table: "CustomerQuotes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceBreakdownJson",
                table: "CustomerQuotes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Audience",
                table: "CustomerQuoteLines",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Customer");

            migrationBuilder.AddColumn<string>(
                name: "ContractorTier",
                table: "CustomerQuoteLines",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "CustomerQuoteLines",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingLabel",
                table: "CustomerQuoteLines",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Customer");

            migrationBuilder.AddColumn<string>(
                name: "ProductHandle",
                table: "CustomerQuoteLines",
                type: "character varying(180)",
                maxLength: 180,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShopifyVariantIdSnapshot",
                table: "CustomerQuoteLines",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Vendor",
                table: "CustomerQuoteLines",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QuoteConfiguration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnableCalculatedRates = table.Column<bool>(type: "boolean", nullable: false),
                    UseTestFlatRate = table.Column<bool>(type: "boolean", nullable: false),
                    TestFlatRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EnableRemoteSurcharge = table.Column<bool>(type: "boolean", nullable: false),
                    ShowVendorSource = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultTaxRate = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    DefaultRatePerMinute = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    MaximumDeliveryRadiusMiles = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    OutsideRadiusPhone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DefaultOriginLabel = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DefaultOriginAddress = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteConfiguration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuoteMaterialRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SkuPrefix = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    MaterialName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TruckCapacity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    VendorSource = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteMaterialRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuoteOriginAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteOriginAddresses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteMaterialRules_SkuPrefix",
                table: "QuoteMaterialRules",
                column: "SkuPrefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteOriginAddresses_Label",
                table: "QuoteOriginAddresses",
                column: "Label",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuoteConfiguration");

            migrationBuilder.DropTable(
                name: "QuoteMaterialRules");

            migrationBuilder.DropTable(
                name: "QuoteOriginAddresses");

            migrationBuilder.DropColumn(
                name: "ContractorTier1Price",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "ContractorTier2Price",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "UnitLabel",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "BillingAddressLine1",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "BillingAddressLine2",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "BillingCity",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "BillingCountry",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "BillingPostalCode",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "BillingState",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "CalculatedDeliveryAmount",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "ContractorTier",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "CustomDeliveryAmount",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "DeliveryDescription",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "DeliveryEta",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "DeliveryServiceName",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "DeliverySummary",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "PaymentTermsDueInDays",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "PaymentTermsName",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "PaymentTermsTemplateId",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "RatePerMinute",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "ShippingQuantity",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "ShippingRate",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "ShippingUnit",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "ShopifyCompanyContactId",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "ShopifyCompanyId",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "ShopifyCompanyLocationId",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "ShopifyDraftOrderUrl",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "SourceBreakdownJson",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "CustomerQuoteLines");

            migrationBuilder.DropColumn(
                name: "ContractorTier",
                table: "CustomerQuoteLines");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "CustomerQuoteLines");

            migrationBuilder.DropColumn(
                name: "PricingLabel",
                table: "CustomerQuoteLines");

            migrationBuilder.DropColumn(
                name: "ProductHandle",
                table: "CustomerQuoteLines");

            migrationBuilder.DropColumn(
                name: "ShopifyVariantIdSnapshot",
                table: "CustomerQuoteLines");

            migrationBuilder.DropColumn(
                name: "Vendor",
                table: "CustomerQuoteLines");
        }
    }
}
