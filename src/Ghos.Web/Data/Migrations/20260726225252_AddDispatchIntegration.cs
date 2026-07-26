using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DispatchConnectionSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EncryptedIntegrationSecret = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    LastCursor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastSyncStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncCompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSuccessfulSyncAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    LastSyncMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastImportedCount = table.Column<int>(type: "integer", nullable: false),
                    LastCreatedCount = table.Column<int>(type: "integer", nullable: false),
                    LastUpdatedCount = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchConnectionSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DispatchConnectionSettings");
        }
    }
}
