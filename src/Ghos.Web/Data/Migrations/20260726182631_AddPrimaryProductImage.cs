using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrimaryProductImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                table: "AssetProductLinks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrimaryAssignedAtUtc",
                table: "AssetProductLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryAssignedByUserId",
                table: "AssetProductLinks",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetProductLinks_ProductId_IsPrimary",
                table: "AssetProductLinks",
                columns: new[] { "ProductId", "IsPrimary" },
                unique: true,
                filter: "\"IsPrimary\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AssetProductLinks_ProductId_IsPrimary",
                table: "AssetProductLinks");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                table: "AssetProductLinks");

            migrationBuilder.DropColumn(
                name: "PrimaryAssignedAtUtc",
                table: "AssetProductLinks");

            migrationBuilder.DropColumn(
                name: "PrimaryAssignedByUserId",
                table: "AssetProductLinks");
        }
    }
}
