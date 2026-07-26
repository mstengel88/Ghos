using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderDeliveryOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ExternalOrderId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    OrderNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Source = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Contact = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    DeliveryAddress = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    DeliveryCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeliveryState = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    DeliveryPostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RequestedWindow = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    TimePreference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SourceCreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourceUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalDispatchId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternalRouteId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RouteCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Truck = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DriverName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    StopSequence = table.Column<int>(type: "integer", nullable: true),
                    Material = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ScheduledForUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Eta = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TravelMinutes = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    TravelMiles = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    DepartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArrivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProofName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    ProofNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ProofPhotoCount = table.Column<int>(type: "integer", nullable: false),
                    SourceCreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourceUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deliveries_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_ExternalDispatchId",
                table: "Deliveries",
                column: "ExternalDispatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_SalesOrderId",
                table: "Deliveries",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_ScheduledForUtc",
                table: "Deliveries",
                column: "ScheduledForUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_Status_ScheduledForUtc",
                table: "Deliveries",
                columns: new[] { "Status", "ScheduledForUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_ExternalKey",
                table: "SalesOrders",
                column: "ExternalKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_OrderNumber",
                table: "SalesOrders",
                column: "OrderNumber");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_Status_UpdatedAtUtc",
                table: "SalesOrders",
                columns: new[] { "Status", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Deliveries");

            migrationBuilder.DropTable(
                name: "SalesOrders");
        }
    }
}
