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

    public DbSet<ImageRow> Images => Set<ImageRow>();

    public DbSet<IgdbGameRow> IgdbGames => Set<IgdbGameRow>();

    public DbSet<CoverChoiceRow> CoverChoices => Set<CoverChoiceRow>();

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
            // One canonical record per IGDB game, for the merges planned later; the records without an
            // id, all of them today, are not concerned.
            game.HasIndex(g => g.IgdbId).IsUnique();
        });

        modelBuilder.Entity<StoreEntryRow>(entry =>
        {
            entry.ToTable("store_entries");
            entry.Property(e => e.Source).HasMaxLength(Limits.Source);
            entry.Property(e => e.ExternalId).HasMaxLength(Limits.ExternalId);
            entry.Property(e => e.Name).HasMaxLength(Limits.Name);
            entry.Property(e => e.CoverPath).HasMaxLength(Limits.CoverPath);
            entry.HasIndex(e => new { e.Source, e.ExternalId }).IsUnique();
            // A store's entries IGDB is to be asked about: never asked first, then the oldest asked.
            entry.HasIndex(e => new { e.Source, e.IgdbAskedAt });
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
            ownership.ToTable("ownerships", table =>
            {
                table.HasCheckConstraint("ck_ownerships_playtime", "playtime_seconds >= 0 AND recent_playtime_seconds >= 0");

                // Only a game out of the library is listed again, or has a notice to dismiss.
                table.HasCheckConstraint(
                    "ck_ownerships_listed_again",
                    "unlisted_since IS NOT NULL OR (listed_again_since IS NULL AND notice_dismissed_at IS NULL)");
            });
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

        modelBuilder.Entity<ImageRow>(image =>
        {
            image.ToTable("images");
            // One image per game, kind and slot; a file is found again by its name, to know whether
            // another game or slot still shows it before it is removed.
            image.HasKey(i => new { i.GameId, i.Kind, i.Slot });
            image.Property(i => i.Kind).HasMaxLength(Limits.ImageKind);
            image.Property(i => i.Slot).HasMaxLength(Limits.Source);
            image.Property(i => i.Source).HasMaxLength(Limits.Source);
            image.Property(i => i.Key).HasMaxLength(Limits.ImageKey);
            image.Property(i => i.ContentType).HasMaxLength(Limits.ContentType);
            image.HasIndex(i => new { i.Kind, i.Source, i.Key });
            image.HasOne<GameRow>().WithMany().HasForeignKey(i => i.GameId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IgdbGameRow>(game =>
        {
            game.ToTable("igdb_games", table => table.HasCheckConstraint(
                "ck_igdb_games_time_to_beat",
                "hastily_seconds >= 0 AND normally_seconds >= 0 AND completely_seconds >= 0 AND time_to_beat_count >= 0"));
            game.HasKey(g => g.IgdbId);
            game.Property(g => g.IgdbId).ValueGeneratedNever();
            game.Property(g => g.Name).HasMaxLength(Limits.Name);
            game.Property(g => g.Slug).HasMaxLength(Limits.GameDataWord);
            game.Property(g => g.Url).HasMaxLength(Limits.GameDataUrl);
            game.Property(g => g.CoverImageId).HasMaxLength(Limits.GameDataWord);
            game.Property(g => g.Summary).HasMaxLength(Limits.GameDataSummary);
            // Empty for the games read before IGDB was asked for them, until it is asked again.
            game.Property(g => g.Similar).HasDefaultValueSql("'{}'");
        });

        modelBuilder.Entity<CoverChoiceRow>(choice =>
        {
            choice.ToTable("cover_choices");
            // An owner's choice for a game, one at most: the library reads them all by owner.
            choice.HasKey(c => new { c.OwnerId, c.GameId });
            choice.Property(c => c.Source).HasMaxLength(Limits.Source);
            choice.HasIndex(c => c.GameId);
            choice.HasOne<GameRow>().WithMany().HasForeignKey(c => c.GameId).OnDelete(DeleteBehavior.Restrict);
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
