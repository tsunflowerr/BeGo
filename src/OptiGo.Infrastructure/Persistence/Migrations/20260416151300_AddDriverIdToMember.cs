using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptiGo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverIdToMember : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "driver_id",
                table: "members",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "driver_id",
                table: "members");
        }
    }
}
