using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptiGo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthChatRateLimitIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "auth_email",
                table: "members",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "auth_subject",
                table: "members",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_chat_messages_members_member_id",
                        column: x => x.member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_chat_messages_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_votes_session",
                table: "votes",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "idx_sessions_expires_at",
                table: "sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "idx_members_session_auth_subject",
                table: "members",
                columns: new[] { "session_id", "auth_subject" });

            migrationBuilder.CreateIndex(
                name: "idx_chat_messages_session_created_at",
                table: "chat_messages",
                columns: new[] { "session_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_member_id",
                table: "chat_messages",
                column: "member_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropIndex(
                name: "idx_votes_session",
                table: "votes");

            migrationBuilder.DropIndex(
                name: "idx_sessions_expires_at",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "idx_members_session_auth_subject",
                table: "members");

            migrationBuilder.DropColumn(
                name: "auth_email",
                table: "members");

            migrationBuilder.DropColumn(
                name: "auth_subject",
                table: "members");
        }
    }
}
