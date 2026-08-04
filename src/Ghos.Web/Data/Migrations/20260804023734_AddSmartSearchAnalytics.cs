using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartSearchAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmartSearchEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Query = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    NormalizedQuery = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IntentSummary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResultCount = table.Column<int>(type: "integer", nullable: false),
                    SearchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SelectedProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartSearchEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmartSearchEvents_NormalizedQuery",
                table: "SmartSearchEvents",
                column: "NormalizedQuery");

            migrationBuilder.CreateIndex(
                name: "IX_SmartSearchEvents_ResultCount_SearchedAtUtc",
                table: "SmartSearchEvents",
                columns: new[] { "ResultCount", "SearchedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SmartSearchEvents_SearchedAtUtc",
                table: "SmartSearchEvents",
                column: "SearchedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SmartSearchEvents_SelectedProductId",
                table: "SmartSearchEvents",
                column: "SelectedProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmartSearchEvents");
        }
    }
}
