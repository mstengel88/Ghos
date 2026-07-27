using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260727030000_AddDumpSiteIntegration")]
public partial class AddDumpSiteIntegration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DumpSiteConnectionSettings",
            columns: table => new
            {
                Id = table.Column<int>(
                        type: "integer",
                        nullable: false)
                    .Annotation(
                        "Npgsql:ValueGenerationStrategy",
                        Npgsql.EntityFrameworkCore.PostgreSQL.Metadata
                            .NpgsqlValueGenerationStrategy
                            .IdentityByDefaultColumn),
                BridgeApiBaseUrl = table.Column<string>(
                    type: "character varying(500)",
                    maxLength: 500,
                    nullable: false),
                EncryptedSharedSecret = table.Column<string>(
                    type: "character varying(2000)",
                    maxLength: 2000,
                    nullable: false),
                BridgeId = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: false),
                ItemMappingsJson = table.Column<string>(
                    type: "text",
                    nullable: false),
                CompanyMappingsJson = table.Column<string>(
                    type: "text",
                    nullable: false),
                CounterpointLocation = table.Column<string>(
                    type: "character varying(20)",
                    maxLength: 20,
                    nullable: false),
                CounterpointStation = table.Column<string>(
                    type: "character varying(20)",
                    maxLength: 20,
                    nullable: false),
                CounterpointDrawer = table.Column<string>(
                    type: "character varying(20)",
                    maxLength: 20,
                    nullable: false),
                CounterpointSalesRep = table.Column<string>(
                    type: "character varying(30)",
                    maxLength: 30,
                    nullable: false),
                LastHealthCheckAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true),
                LastHealthCheckSucceeded = table.Column<bool>(
                    type: "boolean",
                    nullable: true),
                LastHealthCheckMessage = table.Column<string>(
                    type: "character varying(500)",
                    maxLength: 500,
                    nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false),
                UpdatedByUserId = table.Column<string>(
                    type: "character varying(450)",
                    maxLength: 450,
                    nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_DumpSiteConnectionSettings",
                    item => item.Id);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DumpSiteConnectionSettings");
    }
}
