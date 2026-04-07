using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptiGo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWinningVenueId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "winning_venue_id",
                table: "sessions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "winning_venue_id",
                table: "sessions");
        }
    }
}
