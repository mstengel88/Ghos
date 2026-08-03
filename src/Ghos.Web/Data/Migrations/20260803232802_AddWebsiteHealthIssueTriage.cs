using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWebsiteHealthIssueTriage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgedAtUtc",
                table: "WebsiteHealthIssues",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedByUserId",
                table: "WebsiteHealthIssues",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuppressedAtUtc",
                table: "WebsiteHealthIssues",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuppressedByUserId",
                table: "WebsiteHealthIssues",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TriageNote",
                table: "WebsiteHealthIssues",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcknowledgedAtUtc",
                table: "WebsiteHealthIssues");

            migrationBuilder.DropColumn(
                name: "AcknowledgedByUserId",
                table: "WebsiteHealthIssues");

            migrationBuilder.DropColumn(
                name: "SuppressedAtUtc",
                table: "WebsiteHealthIssues");

            migrationBuilder.DropColumn(
                name: "SuppressedByUserId",
                table: "WebsiteHealthIssues");

            migrationBuilder.DropColumn(
                name: "TriageNote",
                table: "WebsiteHealthIssues");
        }
    }
}
