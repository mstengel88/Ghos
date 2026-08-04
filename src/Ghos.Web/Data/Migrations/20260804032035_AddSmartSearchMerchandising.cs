using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartSearchMerchandising : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmartSearchMerchandisingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QueryPhrase = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedQueryPhrase = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BoostPoints = table.Column<int>(type: "integer", nullable: false),
                    PinPosition = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartSearchMerchandisingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmartSearchMerchandisingRules_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmartSearchMerchandisingRules_IsActive",
                table: "SmartSearchMerchandisingRules",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SmartSearchMerchandisingRules_NormalizedQueryPhrase_Product~",
                table: "SmartSearchMerchandisingRules",
                columns: new[] { "NormalizedQueryPhrase", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmartSearchMerchandisingRules_ProductId",
                table: "SmartSearchMerchandisingRules",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmartSearchMerchandisingRules");
        }
    }
}
