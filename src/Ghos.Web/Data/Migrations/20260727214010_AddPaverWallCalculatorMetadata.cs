using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaverWallCalculatorMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalculatorOrderUnitLabel",
                table: "ProductVariants",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CalculatorUnitHeightInches",
                table: "ProductVariants",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CalculatorUnitLengthInches",
                table: "ProductVariants",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CoveragePerOrderUnitSqFt",
                table: "ProductVariants",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LayersPerPallet",
                table: "ProductVariants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PalletWeightLbs",
                table: "ProductVariants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PiecesPerOrderUnit",
                table: "ProductVariants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SquareFeetPerLayer",
                table: "ProductVariants",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalculatorOrderUnitLabel",
                table: "Products",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CalculatorUnitHeightInches",
                table: "Products",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CalculatorUnitLengthInches",
                table: "Products",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CoveragePerOrderUnitSqFt",
                table: "Products",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LayersPerPallet",
                table: "Products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PalletWeightLbs",
                table: "Products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PiecesPerOrderUnit",
                table: "Products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectCalculatorType",
                table: "Products",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SquareFeetPerLayer",
                table: "Products",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalculatorOrderUnitLabel",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "CalculatorUnitHeightInches",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "CalculatorUnitLengthInches",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "CoveragePerOrderUnitSqFt",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "LayersPerPallet",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "PalletWeightLbs",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "PiecesPerOrderUnit",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "SquareFeetPerLayer",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "CalculatorOrderUnitLabel",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CalculatorUnitHeightInches",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CalculatorUnitLengthInches",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CoveragePerOrderUnitSqFt",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "LayersPerPallet",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PalletWeightLbs",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PiecesPerOrderUnit",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ProjectCalculatorType",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SquareFeetPerLayer",
                table: "Products");
        }
    }
}
