using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace cinescout.persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchingEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PriceAreaProviderId",
                table: "SeatStatuses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriceAreaProviderId",
                table: "SeatingSnapshotSeats",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasSufficientSeats",
                table: "Matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PosterUrl",
                table: "Films",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PerformancePriceAreas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PerformanceId = table.Column<int>(type: "integer", nullable: false),
                    ProviderId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    OrderPrice = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformancePriceAreas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PerformancePriceAreas_Performances_PerformanceId",
                        column: x => x.PerformanceId,
                        principalTable: "Performances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PerformancePriceAreas_PerformanceId_ProviderId",
                table: "PerformancePriceAreas",
                columns: new[] { "PerformanceId", "ProviderId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PerformancePriceAreas");

            migrationBuilder.DropColumn(
                name: "PriceAreaProviderId",
                table: "SeatStatuses");

            migrationBuilder.DropColumn(
                name: "PriceAreaProviderId",
                table: "SeatingSnapshotSeats");

            migrationBuilder.DropColumn(
                name: "HasSufficientSeats",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "PosterUrl",
                table: "Films");
        }
    }
}
