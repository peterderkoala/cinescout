using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cinescout.persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMatchStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "Matches");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Matches",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
