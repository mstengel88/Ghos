using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWebsiteHealthCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonitoredSites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CheckIntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    RequestTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    RequestDelayMilliseconds = table.Column<int>(type: "integer", nullable: false),
                    MaxCrawlPages = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoredSites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebsiteCheckRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MonitoredSiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OverallScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    AvailabilityScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    SecurityScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    DiscoverabilityScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    ContentScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    PagesCrawled = table.Column<int>(type: "integer", nullable: false),
                    LinksChecked = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebsiteCheckRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebsiteCheckRuns_MonitoredSites_MonitoredSiteId",
                        column: x => x.MonitoredSiteId,
                        principalTable: "MonitoredSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebsiteChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MonitoredSiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TargetPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebsiteChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebsiteChecks_MonitoredSites_MonitoredSiteId",
                        column: x => x.MonitoredSiteId,
                        principalTable: "MonitoredSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebsiteHealthIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MonitoredSiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CheckKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AffectedUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Severity = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    FirstDetectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastDetectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSeenRunId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebsiteHealthIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebsiteHealthIssues_MonitoredSites_MonitoredSiteId",
                        column: x => x.MonitoredSiteId,
                        principalTable: "MonitoredSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebsiteHealthMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WebsiteCheckRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    WebsiteCheckId = table.Column<Guid>(type: "uuid", nullable: true),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    NumericValue = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    Unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AffectedUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebsiteHealthMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebsiteHealthMetrics_WebsiteCheckRuns_WebsiteCheckRunId",
                        column: x => x.WebsiteCheckRunId,
                        principalTable: "WebsiteCheckRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WebsiteHealthMetrics_WebsiteChecks_WebsiteCheckId",
                        column: x => x.WebsiteCheckId,
                        principalTable: "WebsiteChecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredSites_BaseUrl",
                table: "MonitoredSites",
                column: "BaseUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredSites_IsEnabled_LastCheckedAtUtc",
                table: "MonitoredSites",
                columns: new[] { "IsEnabled", "LastCheckedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WebsiteCheckRuns_MonitoredSiteId_StartedAtUtc",
                table: "WebsiteCheckRuns",
                columns: new[] { "MonitoredSiteId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WebsiteChecks_MonitoredSiteId_Key",
                table: "WebsiteChecks",
                columns: new[] { "MonitoredSiteId", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_WebsiteChecks_MonitoredSiteId_Key_TargetPath",
                table: "WebsiteChecks",
                columns: new[] { "MonitoredSiteId", "Key", "TargetPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebsiteHealthIssues_MonitoredSiteId_Fingerprint",
                table: "WebsiteHealthIssues",
                columns: new[] { "MonitoredSiteId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebsiteHealthIssues_MonitoredSiteId_ResolvedAtUtc_Severity",
                table: "WebsiteHealthIssues",
                columns: new[] { "MonitoredSiteId", "ResolvedAtUtc", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_WebsiteHealthMetrics_WebsiteCheckId_RecordedAtUtc",
                table: "WebsiteHealthMetrics",
                columns: new[] { "WebsiteCheckId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WebsiteHealthMetrics_WebsiteCheckRunId_Key",
                table: "WebsiteHealthMetrics",
                columns: new[] { "WebsiteCheckRunId", "Key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebsiteHealthIssues");

            migrationBuilder.DropTable(
                name: "WebsiteHealthMetrics");

            migrationBuilder.DropTable(
                name: "WebsiteCheckRuns");

            migrationBuilder.DropTable(
                name: "WebsiteChecks");

            migrationBuilder.DropTable(
                name: "MonitoredSites");
        }
    }
}
