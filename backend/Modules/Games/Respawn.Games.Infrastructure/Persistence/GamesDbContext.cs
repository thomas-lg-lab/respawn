using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using Respawn.SharedKernel;

namespace Respawn.Games.Infrastructure.Persistence;

/// <summary>The tables of the Games module, in its own schema: the catalogue, the linked store accounts,
/// what the sources answered, and who owns and played what.</summary>
public sealed class GamesDbContext(DbContextOptions<GamesDbContext> options) : DbContext(options)
{
    /// <summary>The PostgreSQL schema this module owns. No other module reads or writes it.</summary>
    public const string Schema = "games";

    public DbSet<GameRow> Games => Set<GameRow>();

    public DbSet<StoreEntryRow> StoreEntries => Set<StoreEntryRow>();

    public DbSet<LinkedAccountRow> LinkedAccounts => Set<LinkedAccountRow>();

    public DbSet<RawSnapshotRow> RawSnapshots => Set<RawSnapshotRow>();

    public DbSet<ObservationRow> Observations => Set<ObservationRow>();

    public DbSet<OwnershipRow> Ownerships => Set<OwnershipRow>();

    public DbSet<SyncRunRow> SyncRuns => Set<SyncRunRow>();

    public DbSet<YearReviewRow> YearReviews => Set<YearReviewRow>();

    public DbSet<MonthlyPlaytimeRow> MonthlyPlaytime => Set<MonthlyPlaytimeRow>();

    /// <summary>The one place that says how the context talks to PostgreSQL, shared by the app, the
    /// migration at first connection and the design-time tools.</summary>
    public static void Configure(DbContextOptionsBuilder options, DbDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql
                // The module's migration history lives with its tables, so each module migrates alone.
                .MigrationsHistoryTable("__EFMigrationsHistory", Schema)
                // A pooled connection the server closed meanwhile is only found broken when used: the
                // query is run again on a new one. Short and few, so a database that is really gone
                // still answers 503 quickly.
                .EnableRetryOnFailure(maxRetryCount: 2, maxRetryDelay: TimeSpan.FromMilliseconds(200), errorCodesToAdd: null))
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // Nothing is deleted along with something else: the one use case that deletes,
        // unlinking an account, says itself what goes with what.
        modelBuilder.Entity<GameRow>(game =>
        {
            game.ToTable("games");
            game.Property(g => g.Name).HasMaxLength(Limits.Name);
            // One canonical record per IGDB game; the records without an id are not concerned.
            game.HasIndex(g => g.IgdbId).IsUnique();
        });

        modelBuilder.Entity<StoreEntryRow>(entry =>
        {
            entry.ToTable("store_entries");
            entry.Property(e => e.Source).HasMaxLength(Limits.Source);
            entry.Property(e => e.ExternalId).HasMaxLength(Limits.ExternalId);
            entry.Property(e => e.Name).HasMaxLength(Limits.Name);
            entry.HasIndex(e => new { e.Source, e.ExternalId }).IsUnique();
            entry.HasOne<GameRow>().WithMany().HasForeignKey(e => e.GameId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LinkedAccountRow>(account =>
        {
            account.ToTable("linked_accounts");
            account.Property(a => a.Source).HasMaxLength(Limits.Source);
            account.Property(a => a.ExternalId).HasMaxLength(Limits.ExternalId);
            account.Property(a => a.DisplayName).HasMaxLength(Limits.DisplayName);
            // A store account has one owner: nobody attaches someone else's library.
            account.HasIndex(a => new { a.Source, a.ExternalId }).IsUnique();
            account.HasIndex(a => a.OwnerId);
        });

        modelBuilder.Entity<RawSnapshotRow>(snapshot =>
        {
            snapshot.ToTable("raw_snapshots");
            snapshot.Property(s => s.Source).HasMaxLength(Limits.Source);
            snapshot.Property(s => s.Kind).HasMaxLength(Limits.SnapshotKind);
            snapshot.Property(s => s.Payload).HasColumnType("text");
            snapshot.HasIndex(s => new { s.LinkedAccountId, s.Kind, s.TakenAt });
            snapshot.HasIndex(s => new { s.Source, s.Kind, s.TakenAt });
            // An account's readings in the order they were taken, whatever their kind: the history reads
            // a range of them, and walks back from its first day.
            snapshot.HasIndex(s => new { s.LinkedAccountId, s.TakenAt, s.Id });
            snapshot.HasOne<LinkedAccountRow>().WithMany().HasForeignKey(s => s.LinkedAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ObservationRow>(observation =>
        {
            observation.ToTable("observations", table => table.HasCheckConstraint(
                "ck_observations_playtime", "playtime_seconds >= 0 AND known_playtime_seconds >= 0 AND recent_playtime_seconds >= 0"));
            // By snapshot first: a period is a range of snapshots. By store entry for one game over time.
            observation.HasKey(o => new { o.SnapshotId, o.StoreEntryId });
            observation.HasOne<RawSnapshotRow>().WithMany().HasForeignKey(o => o.SnapshotId).OnDelete(DeleteBehavior.Restrict);
            observation.HasOne<StoreEntryRow>().WithMany().HasForeignKey(o => o.StoreEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OwnershipRow>(ownership =>
        {
            ownership.ToTable("ownerships", table => table.HasCheckConstraint(
                "ck_ownerships_playtime", "playtime_seconds >= 0 AND recent_playtime_seconds >= 0"));
            ownership.HasKey(o => new { o.LinkedAccountId, o.StoreEntryId });
            ownership.HasOne<LinkedAccountRow>().WithMany().HasForeignKey(o => o.LinkedAccountId).OnDelete(DeleteBehavior.Restrict);
            ownership.HasOne<StoreEntryRow>().WithMany().HasForeignKey(o => o.StoreEntryId).OnDelete(DeleteBehavior.Restrict);
            ownership.HasOne<RawSnapshotRow>().WithMany().HasForeignKey(o => o.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SyncRunRow>(run =>
        {
            run.ToTable("sync_runs", table =>
            {
                table.HasCheckConstraint("ck_sync_runs_trigger", "trigger IN ('scheduled', 'manual')");
                table.HasCheckConstraint("ck_sync_runs_outcome", "outcome IN ('succeeded', 'nothing_visible', 'failed')");
                // A failure says why, and nothing else does.
                table.HasCheckConstraint("ck_sync_runs_error", "(outcome = 'failed') = (error IS NOT NULL)");
            });
            run.Property(r => r.Trigger).HasMaxLength(SyncWords.MaxLength);
            run.Property(r => r.Outcome).HasMaxLength(SyncWords.MaxLength);
            run.Property(r => r.Error).HasMaxLength(SyncWords.MaxLength);
            run.Property(r => r.Detail).HasMaxLength(Limits.SyncDetail);
            // The runs of the day, of every account; the last run of one; and the last that succeeded,
            // however many did not since.
            run.HasIndex(r => r.StartedAt);
            run.HasIndex(r => new { r.LinkedAccountId, r.StartedAt });
            run.HasIndex(r => new { r.LinkedAccountId, r.StartedAt }, "ix_sync_runs_linked_account_id_started_at_succeeded")
                .HasDatabaseName("ix_sync_runs_linked_account_id_started_at_succeeded")
                .HasFilter("outcome = 'succeeded'");
            run.HasOne<LinkedAccountRow>().WithMany().HasForeignKey(r => r.LinkedAccountId).OnDelete(DeleteBehavior.Restrict);
            run.HasOne<RawSnapshotRow>().WithMany().HasForeignKey(r => r.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<YearReviewRow>(review =>
        {
            review.ToTable("year_reviews");
            review.HasKey(r => new { r.LinkedAccountId, r.Year });
            review.HasOne<LinkedAccountRow>().WithMany().HasForeignKey(r => r.LinkedAccountId).OnDelete(DeleteBehavior.Restrict);
            review.HasOne<RawSnapshotRow>().WithMany().HasForeignKey(r => r.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MonthlyPlaytimeRow>(month =>
        {
            month.ToTable("monthly_playtime", table =>
            {
                table.HasCheckConstraint("ck_monthly_playtime_figures", "playtime_seconds >= 0 AND sessions >= 0");
                table.HasCheckConstraint("ck_monthly_playtime_month", "EXTRACT(DAY FROM month) = 1");
            });
            // An account's months in order; a year's rows are replaced together, by account and by month.
            month.HasKey(m => new { m.LinkedAccountId, m.Month, m.StoreEntryId });
            month.HasIndex(m => m.SnapshotId);
            month.HasOne<LinkedAccountRow>().WithMany().HasForeignKey(m => m.LinkedAccountId).OnDelete(DeleteBehavior.Restrict);
            month.HasOne<StoreEntryRow>().WithMany().HasForeignKey(m => m.StoreEntryId).OnDelete(DeleteBehavior.Restrict);
            month.HasOne<RawSnapshotRow>().WithMany().HasForeignKey(m => m.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class GamesSchemaMigrator : ISchemaMigrator
{
    public void Migrate(DbDataSource dataSource)
    {
        var options = new DbContextOptionsBuilder<GamesDbContext>();
        GamesDbContext.Configure(options, dataSource);
        using var context = new GamesDbContext(options.Options);
        context.Database.Migrate();
    }
}

/// <summary>For `dotnet ef migrations add` only: the tools need a context without starting the app, and
/// never connect with it.</summary>
internal sealed class GamesDbContextFactory : IDesignTimeDbContextFactory<GamesDbContext>
{
    public GamesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GamesDbContext>();
        GamesDbContext.Configure(options, NpgsqlDataSource.Create("Host=localhost;Database=respawn"));
        return new GamesDbContext(options.Options);
    }
}
