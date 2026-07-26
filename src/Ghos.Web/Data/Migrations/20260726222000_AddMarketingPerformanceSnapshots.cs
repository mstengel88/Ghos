using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260726222000_AddMarketingPerformanceSnapshots")]
public partial class AddMarketingPerformanceSnapshots : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MarketingPerformanceSnapshots",
            columns: table => new
            {
                Id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                MarketingContentPackageId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                CapturedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false),
                FacebookReach = table.Column<int>(
                    type: "integer",
                    nullable: true),
                FacebookEngagements = table.Column<int>(
                    type: "integer",
                    nullable: true),
                InstagramReach = table.Column<int>(
                    type: "integer",
                    nullable: true),
                InstagramEngagements = table.Column<int>(
                    type: "integer",
                    nullable: true),
                WebsiteClicks = table.Column<int>(
                    type: "integer",
                    nullable: true),
                Leads = table.Column<int>(
                    type: "integer",
                    nullable: true),
                Orders = table.Column<int>(
                    type: "integer",
                    nullable: true),
                Revenue = table.Column<decimal>(
                    type: "numeric(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: true),
                Notes = table.Column<string>(
                    type: "character varying(1000)",
                    maxLength: 1000,
                    nullable: true),
                CreatedByUserId = table.Column<string>(
                    type: "character varying(450)",
                    maxLength: 450,
                    nullable: true),
                CreatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_MarketingPerformanceSnapshots",
                    item => item.Id);
                table.ForeignKey(
                    name: "FK_MarketingPerformance_Content",
                    column: item => item.MarketingContentPackageId,
                    principalTable: "MarketingContentPackages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MarketingPerformance_Content_Captured",
            table: "MarketingPerformanceSnapshots",
            columns: new[]
            {
                "MarketingContentPackageId",
                "CapturedAtUtc"
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MarketingPerformanceSnapshots");
    }
}
