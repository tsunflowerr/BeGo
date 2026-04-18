using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptiGo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPickupRequestsAndRoutePreview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "departure_locked_at",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "final_route_snapshot_json",
                table: "sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "latest_optimization_snapshot_json",
                table: "sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mobility_role",
                table: "members",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "SelfTravel");

            migrationBuilder.CreateTable(
                name: "pickup_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    passenger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_driver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pickup_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_pickup_requests_members_accepted_driver_id",
                        column: x => x.accepted_driver_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pickup_requests_members_passenger_id",
                        column: x => x.passenger_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pickup_requests_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_pickup_requests_session_passenger",
                table: "pickup_requests",
                columns: new[] { "session_id", "passenger_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pickup_requests_accepted_driver_id",
                table: "pickup_requests",
                column: "accepted_driver_id");

            migrationBuilder.CreateIndex(
                name: "IX_pickup_requests_passenger_id",
                table: "pickup_requests",
                column: "passenger_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pickup_requests");

            migrationBuilder.DropColumn(
                name: "departure_locked_at",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "final_route_snapshot_json",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "latest_optimization_snapshot_json",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "mobility_role",
                table: "members");
        }
    }
}
