using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartSearchTypoCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrectedQuery",
                table: "SmartSearchEvents",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CorrectionApplied",
                table: "SmartSearchEvents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionSummary",
                table: "SmartSearchEvents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrectedQuery",
                table: "SmartSearchEvents");

            migrationBuilder.DropColumn(
                name: "CorrectionApplied",
                table: "SmartSearchEvents");

            migrationBuilder.DropColumn(
                name: "CorrectionSummary",
                table: "SmartSearchEvents");
        }
    }
}
