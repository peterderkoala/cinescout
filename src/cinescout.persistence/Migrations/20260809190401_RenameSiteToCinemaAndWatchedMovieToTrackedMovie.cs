using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cinescout.persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Hand-edited from the scaffolded output: `dotnet ef migrations add` initially produced a
    /// DropTable/CreateTable pair for Sites→Cinemas and WatchedMovies→TrackedMovies (it has no way
    /// to know a table was renamed rather than removed-and-added), which would have destroyed every
    /// existing row. Postgres tracks tables, columns, and constraints by internal OID, not by name,
    /// so a straight RENAME preserves all data and every dependent foreign key/index without ever
    /// dropping and re-adding them — this migration only ever renames.
    /// </remarks>
    public partial class RenameSiteToCinemaAndWatchedMovieToTrackedMovie : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "Sites",
                newName: "Cinemas");

            migrationBuilder.RenameTable(
                name: "WatchedMovies",
                newName: "TrackedMovies");

            migrationBuilder.RenameColumn(
                name: "ExternalSiteId",
                table: "Cinemas",
                newName: "ExternalCinemaId");

            migrationBuilder.RenameColumn(
                name: "SiteId",
                table: "Rooms",
                newName: "CinemaId");

            migrationBuilder.RenameColumn(
                name: "SiteId",
                table: "Performances",
                newName: "CinemaId");

            migrationBuilder.RenameColumn(
                name: "SiteId",
                table: "Films",
                newName: "CinemaId");

            migrationBuilder.RenameColumn(
                name: "WatchedMovieId",
                table: "Matches",
                newName: "TrackedMovieId");

            migrationBuilder.RenameIndex(
                name: "IX_Sites_ExternalSiteId",
                table: "Cinemas",
                newName: "IX_Cinemas_ExternalCinemaId");

            migrationBuilder.RenameIndex(
                name: "IX_Rooms_SiteId_ExternalAuditoriumId",
                table: "Rooms",
                newName: "IX_Rooms_CinemaId_ExternalAuditoriumId");

            migrationBuilder.RenameIndex(
                name: "IX_Performances_SiteId_SourcePerformanceId",
                table: "Performances",
                newName: "IX_Performances_CinemaId_SourcePerformanceId");

            migrationBuilder.RenameIndex(
                name: "IX_Films_SiteId_ExternalFilmId",
                table: "Films",
                newName: "IX_Films_CinemaId_ExternalFilmId");

            migrationBuilder.RenameIndex(
                name: "IX_Matches_WatchedMovieId",
                table: "Matches",
                newName: "IX_Matches_TrackedMovieId");

            migrationBuilder.Sql(@"ALTER TABLE ""Cinemas"" RENAME CONSTRAINT ""PK_Sites"" TO ""PK_Cinemas"";");
            migrationBuilder.Sql(@"ALTER TABLE ""TrackedMovies"" RENAME CONSTRAINT ""PK_WatchedMovies"" TO ""PK_TrackedMovies"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Films"" RENAME CONSTRAINT ""FK_Films_Sites_SiteId"" TO ""FK_Films_Cinemas_CinemaId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Performances"" RENAME CONSTRAINT ""FK_Performances_Sites_SiteId"" TO ""FK_Performances_Cinemas_CinemaId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Rooms"" RENAME CONSTRAINT ""FK_Rooms_Sites_SiteId"" TO ""FK_Rooms_Cinemas_CinemaId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Matches"" RENAME CONSTRAINT ""FK_Matches_WatchedMovies_WatchedMovieId"" TO ""FK_Matches_TrackedMovies_TrackedMovieId"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""Matches"" RENAME CONSTRAINT ""FK_Matches_TrackedMovies_TrackedMovieId"" TO ""FK_Matches_WatchedMovies_WatchedMovieId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Rooms"" RENAME CONSTRAINT ""FK_Rooms_Cinemas_CinemaId"" TO ""FK_Rooms_Sites_SiteId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Performances"" RENAME CONSTRAINT ""FK_Performances_Cinemas_CinemaId"" TO ""FK_Performances_Sites_SiteId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Films"" RENAME CONSTRAINT ""FK_Films_Cinemas_CinemaId"" TO ""FK_Films_Sites_SiteId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""TrackedMovies"" RENAME CONSTRAINT ""PK_TrackedMovies"" TO ""PK_WatchedMovies"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Cinemas"" RENAME CONSTRAINT ""PK_Cinemas"" TO ""PK_Sites"";");

            migrationBuilder.RenameIndex(
                name: "IX_Matches_TrackedMovieId",
                table: "Matches",
                newName: "IX_Matches_WatchedMovieId");

            migrationBuilder.RenameIndex(
                name: "IX_Films_CinemaId_ExternalFilmId",
                table: "Films",
                newName: "IX_Films_SiteId_ExternalFilmId");

            migrationBuilder.RenameIndex(
                name: "IX_Performances_CinemaId_SourcePerformanceId",
                table: "Performances",
                newName: "IX_Performances_SiteId_SourcePerformanceId");

            migrationBuilder.RenameIndex(
                name: "IX_Rooms_CinemaId_ExternalAuditoriumId",
                table: "Rooms",
                newName: "IX_Rooms_SiteId_ExternalAuditoriumId");

            migrationBuilder.RenameIndex(
                name: "IX_Cinemas_ExternalCinemaId",
                table: "Cinemas",
                newName: "IX_Sites_ExternalSiteId");

            migrationBuilder.RenameColumn(
                name: "TrackedMovieId",
                table: "Matches",
                newName: "WatchedMovieId");

            migrationBuilder.RenameColumn(
                name: "CinemaId",
                table: "Films",
                newName: "SiteId");

            migrationBuilder.RenameColumn(
                name: "CinemaId",
                table: "Performances",
                newName: "SiteId");

            migrationBuilder.RenameColumn(
                name: "CinemaId",
                table: "Rooms",
                newName: "SiteId");

            migrationBuilder.RenameColumn(
                name: "ExternalCinemaId",
                table: "Cinemas",
                newName: "ExternalSiteId");

            migrationBuilder.RenameTable(
                name: "TrackedMovies",
                newName: "WatchedMovies");

            migrationBuilder.RenameTable(
                name: "Cinemas",
                newName: "Sites");
        }
    }
}
