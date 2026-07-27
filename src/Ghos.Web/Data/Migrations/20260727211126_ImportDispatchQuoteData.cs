using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportDispatchQuoteData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DispatchDataLastCompanyCount",
                table: "QuoteConfiguration",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DispatchDataLastProductCount",
                table: "QuoteConfiguration",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DispatchDataLastQuoteCount",
                table: "QuoteConfiguration",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DispatchDataLastSyncedAtUtc",
                table: "QuoteConfiguration",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupVendor",
                table: "ProductVariants",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QuoteB2BCompanies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ShopifyCompanyId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ShopifyCompanyContactId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ShopifyCompanyLocationId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CompanyName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ContractorTier = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CatalogTitles = table.Column<string>(type: "text", nullable: true),
                    ContactName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Email = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    BillingAddressLine1 = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    BillingAddressLine2 = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    BillingCity = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    BillingState = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    BillingPostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BillingCountry = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    IsTaxExempt = table.Column<bool>(type: "boolean", nullable: false),
                    PaymentTermsName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    PaymentTermsTemplateId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PaymentTermsDueInDays = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteB2BCompanies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteB2BCompanies_CompanyName",
                table: "QuoteB2BCompanies",
                column: "CompanyName");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteB2BCompanies_ExternalId",
                table: "QuoteB2BCompanies",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteB2BCompanies_ShopifyCompanyId",
                table: "QuoteB2BCompanies",
                column: "ShopifyCompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuoteB2BCompanies");

            migrationBuilder.DropColumn(
                name: "DispatchDataLastCompanyCount",
                table: "QuoteConfiguration");

            migrationBuilder.DropColumn(
                name: "DispatchDataLastProductCount",
                table: "QuoteConfiguration");

            migrationBuilder.DropColumn(
                name: "DispatchDataLastQuoteCount",
                table: "QuoteConfiguration");

            migrationBuilder.DropColumn(
                name: "DispatchDataLastSyncedAtUtc",
                table: "QuoteConfiguration");

            migrationBuilder.DropColumn(
                name: "PickupVendor",
                table: "ProductVariants");
        }
    }
}
