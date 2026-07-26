using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260726214500_AddMarketingPublicationTracking")]
public partial class AddMarketingPublicationTracking : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FacebookPublishedUrl",
            table: "MarketingContentPackages",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "InstagramPublishedUrl",
            table: "MarketingContentPackages",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "PublishedAtUtc",
            table: "MarketingContentPackages",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PublishedByUserId",
            table: "MarketingContentPackages",
            type: "character varying(450)",
            maxLength: 450,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PublicationNotes",
            table: "MarketingContentPackages",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FacebookPublishedUrl",
            table: "MarketingContentPackages");
        migrationBuilder.DropColumn(
            name: "InstagramPublishedUrl",
            table: "MarketingContentPackages");
        migrationBuilder.DropColumn(
            name: "PublishedAtUtc",
            table: "MarketingContentPackages");
        migrationBuilder.DropColumn(
            name: "PublishedByUserId",
            table: "MarketingContentPackages");
        migrationBuilder.DropColumn(
            name: "PublicationNotes",
            table: "MarketingContentPackages");
    }
}
