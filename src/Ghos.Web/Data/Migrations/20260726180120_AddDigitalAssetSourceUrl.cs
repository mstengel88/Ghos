using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDigitalAssetSourceUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "DigitalAssets",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_SourceUrl",
                table: "DigitalAssets",
                column: "SourceUrl",
                unique: true,
                filter: "\"SourceUrl\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DigitalAssets_SourceUrl",
                table: "DigitalAssets");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "DigitalAssets");
        }
    }
}
