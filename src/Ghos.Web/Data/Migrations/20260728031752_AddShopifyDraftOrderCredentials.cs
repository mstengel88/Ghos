using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShopifyDraftOrderCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DraftOrderUpdatedAtUtc",
                table: "ShopifyConnectionSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DraftOrderUpdatedByUserId",
                table: "ShopifyConnectionSettings",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedDraftOrderClientId",
                table: "ShopifyConnectionSettings",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedDraftOrderClientSecret",
                table: "ShopifyConnectionSettings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DraftOrderUpdatedAtUtc",
                table: "ShopifyConnectionSettings");

            migrationBuilder.DropColumn(
                name: "DraftOrderUpdatedByUserId",
                table: "ShopifyConnectionSettings");

            migrationBuilder.DropColumn(
                name: "EncryptedDraftOrderClientId",
                table: "ShopifyConnectionSettings");

            migrationBuilder.DropColumn(
                name: "EncryptedDraftOrderClientSecret",
                table: "ShopifyConnectionSettings");
        }
    }
}
