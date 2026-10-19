using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace cinescout.persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Channel",
                table: "NotificationLogs",
                newName: "ChannelName");

            migrationBuilder.AddColumn<Guid>(
                name: "DispatchId",
                table: "NotificationLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Every pre-existing row was its own single-channel dispatch; a shared all-zero id would
            // collapse them into one Recent Activity entry.
            migrationBuilder.Sql("""UPDATE "NotificationLogs" SET "DispatchId" = gen_random_uuid();""");

            migrationBuilder.AddColumn<int>(
                name: "NotificationChannelId",
                table: "NotificationLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NotificationChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    WebhookUrl = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    AvatarUrl = table.Column<string>(type: "text", nullable: true),
                    Prefix = table.Column<string>(type: "text", nullable: true),
                    Suppress = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Types = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationChannels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_DispatchId",
                table: "NotificationLogs",
                column: "DispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_NotificationChannelId",
                table: "NotificationLogs",
                column: "NotificationChannelId");

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationLogs_NotificationChannels_NotificationChannelId",
                table: "NotificationLogs",
                column: "NotificationChannelId",
                principalTable: "NotificationChannels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NotificationLogs_NotificationChannels_NotificationChannelId",
                table: "NotificationLogs");

            migrationBuilder.DropTable(
                name: "NotificationChannels");

            migrationBuilder.DropIndex(
                name: "IX_NotificationLogs_DispatchId",
                table: "NotificationLogs");

            migrationBuilder.DropIndex(
                name: "IX_NotificationLogs_NotificationChannelId",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "DispatchId",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "NotificationChannelId",
                table: "NotificationLogs");

            migrationBuilder.RenameColumn(
                name: "ChannelName",
                table: "NotificationLogs",
                newName: "Channel");
        }
    }
}
