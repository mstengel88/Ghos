using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260726200050_AddMarketingStudio")]
public partial class AddMarketingStudio : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MarketingContentPackages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Slug = table.Column<string>(
                    type: "character varying(180)",
                    maxLength: 180,
                    nullable: false),
                Title = table.Column<string>(
                    type: "character varying(160)",
                    maxLength: 160,
                    nullable: false),
                Series = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false),
                TemplateKey = table.Column<string>(
                    type: "character varying(80)",
                    maxLength: 80,
                    nullable: false),
                Status = table.Column<string>(
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: false),
                ScheduledForUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true),
                ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                DigitalAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                Headline = table.Column<string>(
                    type: "character varying(120)",
                    maxLength: 120,
                    nullable: false),
                Subheadline = table.Column<string>(
                    type: "character varying(220)",
                    maxLength: 220,
                    nullable: true),
                AlternateName = table.Column<string>(
                    type: "character varying(160)",
                    maxLength: 160,
                    nullable: true),
                FactItems = table.Column<string>(
                    type: "character varying(1200)",
                    maxLength: 1200,
                    nullable: true),
                FacebookCaption = table.Column<string>(
                    type: "character varying(4000)",
                    maxLength: 4000,
                    nullable: false),
                InstagramCaption = table.Column<string>(
                    type: "character varying(2200)",
                    maxLength: 2200,
                    nullable: false),
                StoryPrompt = table.Column<string>(
                    type: "character varying(600)",
                    maxLength: 600,
                    nullable: false),
                ReelScript = table.Column<string>(
                    type: "character varying(4000)",
                    maxLength: 4000,
                    nullable: false),
                Hashtags = table.Column<string>(
                    type: "character varying(1000)",
                    maxLength: 1000,
                    nullable: true),
                CallToAction = table.Column<string>(
                    type: "character varying(220)",
                    maxLength: 220,
                    nullable: false),
                DestinationUrl = table.Column<string>(
                    type: "character varying(2048)",
                    maxLength: 2048,
                    nullable: true),
                ApprovedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true),
                ApprovedByUserId = table.Column<string>(
                    type: "character varying(450)",
                    maxLength: 450,
                    nullable: true),
                CreatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false),
                CreatedByUserId = table.Column<string>(
                    type: "character varying(450)",
                    maxLength: 450,
                    nullable: true),
                UpdatedByUserId = table.Column<string>(
                    type: "character varying(450)",
                    maxLength: 450,
                    nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MarketingContentPackages", x => x.Id);
                table.ForeignKey(
                    name: "FK_MarketingContentPackages_DigitalAssets_DigitalAssetId",
                    column: x => x.DigitalAssetId,
                    principalTable: "DigitalAssets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_MarketingContentPackages_Products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MarketingContentPackages_DigitalAssetId",
            table: "MarketingContentPackages",
            column: "DigitalAssetId");
        migrationBuilder.CreateIndex(
            name: "IX_MarketingContentPackages_ProductId",
            table: "MarketingContentPackages",
            column: "ProductId");
        migrationBuilder.CreateIndex(
            name: "IX_MarketingContentPackages_ScheduledForUtc",
            table: "MarketingContentPackages",
            column: "ScheduledForUtc");
        migrationBuilder.CreateIndex(
            name: "IX_MarketingContentPackages_Slug",
            table: "MarketingContentPackages",
            column: "Slug",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_MarketingContentPackages_Status_ScheduledForUtc",
            table: "MarketingContentPackages",
            columns: new[] { "Status", "ScheduledForUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MarketingContentPackages");
    }
}
