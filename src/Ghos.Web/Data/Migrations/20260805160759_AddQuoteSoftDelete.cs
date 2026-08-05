using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "CustomerQuotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "CustomerQuotes",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "CustomerQuotes");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "CustomerQuotes");
        }
    }
}
