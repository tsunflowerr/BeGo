using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptiGo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixDriverIdColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DriverId",
                table: "members",
                newName: "driver_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "driver_id",
                table: "members",
                newName: "DriverId");
        }
    }
}
