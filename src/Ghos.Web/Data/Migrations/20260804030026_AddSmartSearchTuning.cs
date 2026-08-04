using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartSearchTuning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmartSearchSynonymRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Phrase = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedPhrase = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Expansion = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedExpansion = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartSearchSynonymRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmartSearchSynonymRules_IsActive",
                table: "SmartSearchSynonymRules",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SmartSearchSynonymRules_NormalizedPhrase_NormalizedExpansion",
                table: "SmartSearchSynonymRules",
                columns: new[] { "NormalizedPhrase", "NormalizedExpansion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmartSearchSynonymRules");
        }
    }
}
