using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteDeliveryCapacityModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CapacityUnit",
                table: "QuoteMaterialRules",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "quantity");

            migrationBuilder.AddColumn<string>(
                name: "DeliveryMode",
                table: "QuoteMaterialRules",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "bulk");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CapacityUnit",
                table: "QuoteMaterialRules");

            migrationBuilder.DropColumn(
                name: "DeliveryMode",
                table: "QuoteMaterialRules");
        }
    }
}
