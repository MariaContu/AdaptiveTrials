using AdaptiveTrials.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AdaptiveTrials.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Player> Players => Set<Player>();
    public DbSet<GameSession> Sessions => Set<GameSession>();
    public DbSet<Mission> Missions => Set<Mission>();
    public DbSet<BehaviorEvent> BehaviorEvents => Set<BehaviorEvent>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<NormalizedProfile> NormalizedProfiles => Set<NormalizedProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Player>()
            .HasOne(p => p.NormalizedProfile)
            .WithOne(np => np.Player)
            .HasForeignKey<NormalizedProfile>(np => np.PlayerId);

        modelBuilder.Entity<Player>()
            .HasMany(p => p.Sessions)
            .WithOne(s => s.Player)
            .HasForeignKey(s => s.PlayerId);

        modelBuilder.Entity<GameSession>()
            .HasMany(s => s.BehaviorEvents)
            .WithOne(e => e.Session)
            .HasForeignKey(e => e.SessionId);

        modelBuilder.Entity<GameSession>()
            .HasMany(s => s.Recommendations)
            .WithOne(r => r.Session)
            .HasForeignKey(r => r.SessionId);

        modelBuilder.Entity<Mission>()
            .HasMany(m => m.BehaviorEvents)
            .WithOne(e => e.Mission)
            .HasForeignKey(e => e.MissionId);

        modelBuilder.Entity<Mission>()
            .HasMany(m => m.Recommendations)
            .WithOne(r => r.Mission)
            .HasForeignKey(r => r.MissionId)
            .IsRequired(false);
    }
}