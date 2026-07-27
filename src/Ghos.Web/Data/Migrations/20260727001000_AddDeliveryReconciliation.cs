using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Ghos.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260727001000_AddDeliveryReconciliation")]
public partial class AddDeliveryReconciliation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "ReconciledAtUtc",
            table: "Deliveries",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ReconciledByUserId",
            table: "Deliveries",
            type: "character varying(450)",
            maxLength: 450,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ReconciledStatusOverride",
            table: "Deliveries",
            type: "character varying(24)",
            maxLength: 24,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ReconciliationNote",
            table: "Deliveries",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ReconciledAtUtc",
            table: "Deliveries");

        migrationBuilder.DropColumn(
            name: "ReconciledByUserId",
            table: "Deliveries");

        migrationBuilder.DropColumn(
            name: "ReconciledStatusOverride",
            table: "Deliveries");

        migrationBuilder.DropColumn(
            name: "ReconciliationNote",
            table: "Deliveries");
    }
}
