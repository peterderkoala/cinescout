using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cinescout.persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationLogFilmId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FilmId",
                table: "NotificationLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_FilmId",
                table: "NotificationLogs",
                column: "FilmId");

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationLogs_Films_FilmId",
                table: "NotificationLogs",
                column: "FilmId",
                principalTable: "Films",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NotificationLogs_Films_FilmId",
                table: "NotificationLogs");

            migrationBuilder.DropIndex(
                name: "IX_NotificationLogs_FilmId",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "FilmId",
                table: "NotificationLogs");
        }
    }
}
