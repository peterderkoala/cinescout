using cinescout.model;
using Microsoft.EntityFrameworkCore;

namespace cinescout.persistence;

public class CineScoutDbContext(DbContextOptions<CineScoutDbContext> options) : DbContext(options)
{
    public DbSet<Cinema> Cinemas => Set<Cinema>();
    public DbSet<Film> Films => Set<Film>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Performance> Performances => Set<Performance>();
    public DbSet<PerformanceSnapshot> PerformanceSnapshots => Set<PerformanceSnapshot>();
    public DbSet<SeatStatus> SeatStatuses => Set<SeatStatus>();
    public DbSet<PerformancePriceArea> PerformancePriceAreas => Set<PerformancePriceArea>();
    public DbSet<SeatingSnapshot> SeatingSnapshots => Set<SeatingSnapshot>();
    public DbSet<SeatingSnapshotSeat> SeatingSnapshotSeats => Set<SeatingSnapshotSeat>();
    public DbSet<TrackedMovie> TrackedMovies => Set<TrackedMovie>();
    public DbSet<FavoriteTimeWindow> FavoriteTimeWindows => Set<FavoriteTimeWindow>();
    public DbSet<FavoriteSeatMatrix> FavoriteSeatMatrices => Set<FavoriteSeatMatrix>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Cinema>(e =>
        {
            e.HasIndex(s => s.ExternalCinemaId).IsUnique();
        });

        modelBuilder.Entity<Film>(e =>
        {
            e.HasIndex(f => new { f.CinemaId, f.ExternalFilmId }).IsUnique();
            e.HasOne<Cinema>().WithMany().HasForeignKey(f => f.CinemaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Room>(e =>
        {
            e.HasIndex(r => new { r.CinemaId, r.ExternalAuditoriumId }).IsUnique();
            e.HasOne<Cinema>().WithMany().HasForeignKey(r => r.CinemaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Performance>(e =>
        {
            e.HasIndex(p => new { p.CinemaId, p.SourcePerformanceId }).IsUnique();
            e.HasOne<Film>().WithMany().HasForeignKey(p => p.FilmId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Cinema>().WithMany().HasForeignKey(p => p.CinemaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Room>().WithMany().HasForeignKey(p => p.RoomId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PerformanceSnapshot>(e =>
        {
            e.Property(p => p.RawPayload).HasColumnType("jsonb");
            e.HasOne<Performance>().WithMany().HasForeignKey(p => p.PerformanceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SeatStatus>(e =>
        {
            e.HasIndex(s => new { s.PerformanceId, s.SourceSeatId }).IsUnique();
            e.HasOne<Performance>().WithMany().HasForeignKey(s => s.PerformanceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PerformancePriceArea>(e =>
        {
            e.HasIndex(p => new { p.PerformanceId, p.ProviderId }).IsUnique();
            e.Property(p => p.OrderPrice).HasPrecision(10, 4);
            e.HasOne<Performance>().WithMany().HasForeignKey(p => p.PerformanceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SeatingSnapshot>(e =>
        {
            e.Property(s => s.RawPayload).HasColumnType("jsonb");
            e.HasOne<Performance>().WithMany().HasForeignKey(s => s.PerformanceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SeatingSnapshotSeat>(e =>
        {
            e.HasOne<SeatingSnapshot>().WithMany().HasForeignKey(s => s.SnapshotId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrackedMovie>(e =>
        {
            e.HasIndex(w => w.FilmId).IsUnique();
            e.HasOne<Film>().WithMany().HasForeignKey(w => w.FilmId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FavoriteSeatMatrix>(e =>
        {
            e.HasOne<Room>().WithMany().HasForeignKey(m => m.RoomId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Film>().WithMany().HasForeignKey(m => m.FilmId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Match>(e =>
        {
            e.HasOne<Performance>().WithMany().HasForeignKey(m => m.PerformanceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TrackedMovie>().WithMany().HasForeignKey(m => m.TrackedMovieId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationLog>(e =>
        {
            e.HasOne<Match>().WithMany().HasForeignKey(n => n.MatchId).OnDelete(DeleteBehavior.Cascade);
        });

        // Seeded ahead of first use, not inserted by the setup flow — PasswordHash == null is the
        // sole first-run signal. See ADR 0001.
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.HasData(new User { Id = 1, Username = "cinescout" });
        });
    }
}
