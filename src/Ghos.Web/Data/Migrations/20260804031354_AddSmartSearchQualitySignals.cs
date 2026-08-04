using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartSearchQualitySignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TopResultConfidence",
                table: "SmartSearchEvents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TopResultTitle",
                table: "SmartSearchEvents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnmatchedIntentSummary",
                table: "SmartSearchEvents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TopResultConfidence",
                table: "SmartSearchEvents");

            migrationBuilder.DropColumn(
                name: "TopResultTitle",
                table: "SmartSearchEvents");

            migrationBuilder.DropColumn(
                name: "UnmatchedIntentSummary",
                table: "SmartSearchEvents");
        }
    }
}
