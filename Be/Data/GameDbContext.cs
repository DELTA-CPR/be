using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Be.Data;

public class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<GameSession> Sessions => Set<GameSession>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<SpawnPoint> SpawnPoints => Set<SpawnPoint>();
    public DbSet<Elimination> Eliminations => Set<Elimination>();
    public DbSet<ShotRecord> ShotRecords => Set<ShotRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameSession>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasMaxLength(64);
            e.Property(s => s.Code).HasMaxLength(16);
            e.HasIndex(s => s.Code).IsUnique();
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(s => s.EndReason).HasConversion<string>().HasMaxLength(16);
            e.Property(s => s.AdminSubject).HasMaxLength(64);
            e.Property(s => s.AdminPlayerId).HasMaxLength(64);
            e.HasIndex(s => s.Status);
            e.ComplexProperty(s => s.Config, c =>
            {
                c.Property(x => x.RespawnMode).HasConversion<string>().HasMaxLength(16);
                c.Property(x => x.EliminationVisibility).HasConversion<string>().HasMaxLength(16);
            });
            e.HasMany(s => s.Teams).WithOne().HasForeignKey(t => t.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.HasKey(t => new { t.SessionId, t.TeamId });
            e.Property(t => t.TeamId).HasMaxLength(32);
            e.Property(t => t.Name).HasMaxLength(64);
            e.Property(t => t.Color).HasMaxLength(16);
        });

        modelBuilder.Entity<Player>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasMaxLength(64);
            e.Property(p => p.Nickname).HasMaxLength(32);
            e.Property(p => p.TeamId).HasMaxLength(32);
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(p => new { p.SessionId, p.MarkerId }).IsUnique();
            e.HasIndex(p => new { p.SessionId, p.Nickname }).IsUnique();
            e.HasIndex(p => new { p.Status, p.RespawnAt });
            e.HasOne<GameSession>().WithMany().HasForeignKey(p => p.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Team>().WithMany().HasForeignKey(p => new { p.SessionId, p.TeamId }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SpawnPoint>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasMaxLength(64);
            e.Property(s => s.Name).HasMaxLength(64);
            e.Property(s => s.TeamId).HasMaxLength(32);
            e.HasIndex(s => new { s.SessionId, s.MarkerId }).IsUnique();
            e.HasOne<GameSession>().WithMany().HasForeignKey(s => s.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Elimination>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ShotId).HasMaxLength(128);
            e.Property(x => x.ShooterId).HasMaxLength(64);
            e.Property(x => x.TargetId).HasMaxLength(64);
            e.Property(x => x.ShooterTeamId).HasMaxLength(32);
            e.Property(x => x.TargetTeamId).HasMaxLength(32);
            // One shot can eliminate a given target at most once.
            e.HasIndex(x => new { x.SessionId, x.ShooterId, x.ShotId, x.TargetId }).IsUnique();
            e.HasIndex(x => new { x.SessionId, x.CountsForScore, x.ShooterTeamId });
            e.HasOne<GameSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShotRecord>(e =>
        {
            // Idempotency key: a repeated shotId from the same shooter in the same session is a retry.
            e.HasKey(x => new { x.SessionId, x.ShooterId, x.ShotId });
            e.Property(x => x.ShotId).HasMaxLength(128);
            e.Property(x => x.ShooterId).HasMaxLength(64);
            e.Property(x => x.ResultJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.SessionId, x.ShooterId, x.ReceivedAt });
            e.HasOne<GameSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>Used only by <c>dotnet ef</c> to build migrations; never connects.</summary>
public sealed class GameDbContextDesignTimeFactory : IDesignTimeDbContextFactory<GameDbContext>
{
    public GameDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<GameDbContext>().UseNpgsql("Host=localhost;Database=design_time").Options);
}
