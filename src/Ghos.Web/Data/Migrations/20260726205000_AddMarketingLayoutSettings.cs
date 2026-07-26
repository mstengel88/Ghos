using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260726205000_AddMarketingLayoutSettings")]
public partial class AddMarketingLayoutSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LayoutSettingsJson",
            table: "MarketingContentPackages",
            type: "character varying(12000)",
            maxLength: 12000,
            nullable: false,
            defaultValue: "{}");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LayoutSettingsJson",
            table: "MarketingContentPackages");
    }
}
