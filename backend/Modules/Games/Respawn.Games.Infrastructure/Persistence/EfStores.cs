using Microsoft.EntityFrameworkCore;
using Respawn.Games.Ports;

namespace Respawn.Games.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(GamesDbContext db) : IUnitOfWork
{
    /// <summary>The advisory lock every write to the catalogue takes; any number, as long as it is
    /// always this one and no other module's.</summary>
    private const long CatalogueLock = 7306;

    public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken ct) =>
        // Inside another run already: its transaction, its lock, and its retries.
        db.Database.CurrentTransaction is not null
            ? work()
            : RunInTransactionAsync(work, ct);

    private Task<T> RunInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct) =>
        // The context retries on a lost connection (GamesDbContext.Configure), which EF only allows for a
        // transaction when the whole of it can be run again: hence the delegate.
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // nothing of an attempt that failed half-way
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Released by PostgreSQL itself when the transaction ends, committed or not.
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({CatalogueLock})", ct);
            var result = await work();
            await transaction.CommitAsync(ct);
            return result;
        });
}

internal sealed class EfLinkedAccountStore(GamesDbContext db) : ILinkedAccountStore
{
    public async Task<LinkedAccount?> FindAsync(string source, string externalId, CancellationToken ct) =>
        await Accounts(db.LinkedAccounts.Where(a => a.Source == source && a.ExternalId == externalId)).FirstOrDefaultAsync(ct);

    public async Task<LinkedAccount?> FindByIdAsync(Guid id, CancellationToken ct) =>
        await Accounts(db.LinkedAccounts.Where(a => a.Id == id)).FirstOrDefaultAsync(ct);

    public async Task AddAsync(LinkedAccount account, CancellationToken ct)
    {
        db.LinkedAccounts.Add(new LinkedAccountRow
        {
            Id = account.Id,
            OwnerId = account.OwnerId,
            Source = account.Source,
            ExternalId = account.ExternalId,
            DisplayName = account.DisplayName,
            LinkedAt = account.LinkedAt,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LinkedAccount>> ListAsync(Guid ownerId, CancellationToken ct) =>
        await Accounts(db.LinkedAccounts.Where(a => a.OwnerId == ownerId)).ToListAsync(ct);

    public async Task<IReadOnlyList<LinkedAccount>> ListAllAsync(CancellationToken ct) =>
        await Accounts(db.LinkedAccounts).ToListAsync(ct);

    public async Task RemoveAsync(Guid id, CancellationToken ct)
    {
        // In the order the foreign keys allow: what points to a snapshot before the snapshots, and
        // the account last. Nothing here is deleted along with something else by the database.
        await db.SyncRuns.Where(r => r.LinkedAccountId == id).ExecuteDeleteAsync(ct);
        await db.MonthlyPlaytime.Where(m => m.LinkedAccountId == id).ExecuteDeleteAsync(ct);
        await db.YearReviews.Where(r => r.LinkedAccountId == id).ExecuteDeleteAsync(ct);
        await db.Ownerships.Where(o => o.LinkedAccountId == id).ExecuteDeleteAsync(ct);
        await db.Observations
            .Where(o => db.RawSnapshots.Any(s => s.Id == o.SnapshotId && s.LinkedAccountId == id))
            .ExecuteDeleteAsync(ct);
        await db.RawSnapshots.Where(s => s.LinkedAccountId == id).ExecuteDeleteAsync(ct);
        await db.LinkedAccounts.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
    }

    /// <summary>Linked accounts as the core knows them. Whether a key was given is asked of the database:
    /// the sealed key itself is only read when a sync opens it.</summary>
    private static IQueryable<LinkedAccount> Accounts(IQueryable<LinkedAccountRow> rows) =>
        rows.AsNoTracking().Select(row => new LinkedAccount(
            row.Id, row.OwnerId, row.Source, row.ExternalId, row.DisplayName, row.LinkedAt, row.Credential != null));
}

internal sealed class EfSnapshotStore(GamesDbContext db) : ISnapshotStore
{
    public async Task AddAsync(RawSnapshot snapshot, CancellationToken ct)
    {
        db.RawSnapshots.Add(new RawSnapshotRow
        {
            Id = snapshot.Id,
            Source = snapshot.Source,
            Kind = snapshot.Kind,
            TakenAt = snapshot.TakenAt,
            RecordedAt = snapshot.RecordedAt,
            LinkedAccountId = snapshot.LinkedAccountId,
            Complete = snapshot.Complete,
            Payload = snapshot.Payload,
        });
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class EfSyncRunStore(GamesDbContext db) : ISyncRunStore
{
    public async Task AddAsync(SyncRun run, CancellationToken ct)
    {
        db.SyncRuns.Add(new SyncRunRow
        {
            Id = run.Id,
            LinkedAccountId = run.LinkedAccountId,
            Trigger = SyncWords.Of(run.Trigger),
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            Outcome = SyncWords.Of(run.Outcome),
            Error = run.Error is { } error ? SyncWords.Of(error) : null,
            Detail = run.Detail,
            SnapshotId = run.SnapshotId,
            OwnCredential = run.OwnCredential,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SyncRun>> ListSinceAsync(DateTimeOffset since, CancellationToken ct) =>
        [.. (await db.SyncRuns.AsNoTracking().Where(r => r.StartedAt >= since).ToListAsync(ct)).Select(ToRun)];

    public Task<SyncRun?> FindLatestAsync(Guid linkedAccountId, CancellationToken ct) =>
        LatestAsync(db.SyncRuns.Where(r => r.LinkedAccountId == linkedAccountId), ct);

    public async Task<IReadOnlyDictionary<Guid, LatestRuns>> FindLatestAsync(IReadOnlyCollection<Guid> linkedAccountIds, CancellationToken ct)
    {
        // One query; each account's two runs come from an index, however many runs it has.
        Guid[] ids = [.. linkedAccountIds];
        var succeeded = SyncWords.Of(SyncOutcome.Succeeded);
        var latest = await db.LinkedAccounts.AsNoTracking()
            .Where(account => ids.Contains(account.Id))
            .Select(account => new
            {
                account.Id,
                Last = db.SyncRuns
                    .Where(r => r.LinkedAccountId == account.Id)
                    .OrderByDescending(r => r.StartedAt)
                    .ThenByDescending(r => r.Id)
                    .FirstOrDefault(),
                LastSucceeded = db.SyncRuns
                    .Where(r => r.LinkedAccountId == account.Id && r.Outcome == succeeded)
                    .OrderByDescending(r => r.StartedAt)
                    .ThenByDescending(r => r.Id)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);
        return latest.ToDictionary(
            runs => runs.Id,
            runs => new LatestRuns(
                runs.Last is { } last ? ToRun(last) : null,
                runs.LastSucceeded is { } success ? ToRun(success) : null));
    }

    private static async Task<SyncRun?> LatestAsync(IQueryable<SyncRunRow> runs, CancellationToken ct) =>
        await runs.AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct) is { } row
            ? ToRun(row)
            : null;

    private static SyncRun ToRun(SyncRunRow row) =>
        new(
            row.Id,
            row.LinkedAccountId,
            SyncWords.Trigger(row.Trigger),
            row.StartedAt,
            row.FinishedAt,
            SyncWords.Outcome(row.Outcome),
            row.Error is { } error ? SyncWords.Error(error) : null,
            row.Detail,
            row.SnapshotId,
            row.OwnCredential);
}

/// <summary>The words a sync run is stored in: the table's own, which a renamed enum must not change.</summary>
internal static class SyncWords
{
    public const int MaxLength = 32;

    public static string Of(SyncTrigger trigger) => trigger switch
    {
        SyncTrigger.Scheduled => "scheduled",
        SyncTrigger.Manual => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, null),
    };

    public static string Of(SyncOutcome outcome) => outcome switch
    {
        SyncOutcome.Succeeded => "succeeded",
        SyncOutcome.NothingVisible => "nothing_visible",
        SyncOutcome.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    public static string Of(SourceError error) => error switch
    {
        SourceError.NotConfigured => "not_configured",
        SourceError.Refused => "refused",
        SourceError.Unavailable => "unavailable",
        SourceError.Unreadable => "unreadable",
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };

    public static SyncTrigger Trigger(string word) => word switch
    {
        "scheduled" => SyncTrigger.Scheduled,
        "manual" => SyncTrigger.Manual,
        _ => throw new InvalidOperationException($"not a sync trigger: {word}"),
    };

    public static SyncOutcome Outcome(string word) => word switch
    {
        "succeeded" => SyncOutcome.Succeeded,
        "nothing_visible" => SyncOutcome.NothingVisible,
        "failed" => SyncOutcome.Failed,
        _ => throw new InvalidOperationException($"not a sync outcome: {word}"),
    };

    public static SourceError Error(string word) => word switch
    {
        "not_configured" => SourceError.NotConfigured,
        "refused" => SourceError.Refused,
        "unavailable" => SourceError.Unavailable,
        "unreadable" => SourceError.Unreadable,
        _ => throw new InvalidOperationException($"not a sync error: {word}"),
    };
}

internal sealed class EfCatalogue(GamesDbContext db) : ICatalogue
{
    public async Task<IReadOnlyDictionary<string, StoreEntry>> FindStoreEntriesAsync(
        string source, IReadOnlyCollection<string> externalIds, CancellationToken ct)
    {
        string[] ids = [.. externalIds];
        return await db.StoreEntries.AsNoTracking()
            .Where(e => e.Source == source && ids.Contains(e.ExternalId))
            .ToDictionaryAsync(e => e.ExternalId, e => new StoreEntry(e.Id, e.Name), StringComparer.Ordinal, ct);
    }

    public async Task AddAsync(IReadOnlyCollection<NewStoreEntry> entries, DateTimeOffset at, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            db.Games.Add(new GameRow { Id = entry.GameId, Name = entry.Name, NameSourceEntryId = entry.Id, CreatedAt = at });
            db.StoreEntries.Add(new StoreEntryRow
            {
                Id = entry.Id,
                GameId = entry.GameId,
                Source = entry.Source,
                ExternalId = entry.ExternalId,
                Name = entry.Name,
                CreatedAt = at,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task RenameAsync(IReadOnlyCollection<StoreEntryRename> renames, CancellationToken ct)
    {
        Guid[] ids = [.. renames.Select(rename => rename.Id)];
        var entries = await db.StoreEntries.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        Guid[] gameIds = [.. entries.Values.Select(e => e.GameId).Distinct()];
        var games = await db.Games.Where(g => gameIds.Contains(g.Id) && g.NameSourceEntryId != null).ToDictionaryAsync(g => g.Id, ct);
        foreach (var rename in renames)
        {
            var entry = entries[rename.Id];
            entry.Name = rename.Name;
            // The record follows the entry it took its name from, and that one only. After a merge the
            // other entries that point to it are renamed alone, even when they bore the same name.
            if (games.TryGetValue(entry.GameId, out var game) && game.NameSourceEntryId == entry.Id)
            {
                game.Name = rename.Name;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CoverEntry>?> FindCoverEntriesAsync(Guid gameId, CancellationToken ct)
    {
        var entries = await db.StoreEntries.AsNoTracking()
            .Where(e => e.GameId == gameId)
            .Select(e => new CoverEntry(e.Id, e.Source, e.ExternalId, e.CoverPath))
            .ToListAsync(ct);

        // A game most often has an entry: the second question is for the id that is no game's.
        return entries.Count > 0 || await db.Games.AnyAsync(g => g.Id == gameId, ct) ? entries : null;
    }

    public async Task<IReadOnlyList<CoverEntry>> ListCoverPathsDueAsync(Guid linkedAccountId, DateTimeOffset askedBefore, CancellationToken ct) =>
        await db.Ownerships.AsNoTracking()
            .Where(o => o.LinkedAccountId == linkedAccountId)
            .Join(db.StoreEntries.AsNoTracking(), o => o.StoreEntryId, e => e.Id, (o, e) => e)
            .Where(e => e.CoverAskedAt == null || e.CoverAskedAt < askedBefore)
            .OrderBy(e => e.Id)
            .Select(e => new CoverEntry(e.Id, e.Source, e.ExternalId, e.CoverPath))
            .ToListAsync(ct);

    public async Task SetCoverPathsAsync(IReadOnlyCollection<CoverPath> paths, DateTimeOffset at, CancellationToken ct)
    {
        Guid[] ids = [.. paths.Select(path => path.StoreEntryId)];
        var entries = await db.StoreEntries.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        foreach (var path in paths)
        {
            if (entries.TryGetValue(path.StoreEntryId, out var entry))
            {
                entry.CoverPath = path.Path;
                entry.CoverAskedAt = at;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}

internal sealed class EfYearReviewStore(GamesDbContext db) : IYearReviewStore
{
    public async Task<IReadOnlySet<int>> ListYearsAsync(Guid linkedAccountId, CancellationToken ct) =>
        (await db.YearReviews.AsNoTracking().Where(r => r.LinkedAccountId == linkedAccountId).Select(r => r.Year).ToListAsync(ct))
            .ToHashSet();

    public async Task RecordAsync(Guid linkedAccountId, int year, Guid snapshotId, IReadOnlyCollection<MonthPlayed> months, CancellationToken ct)
    {
        var (first, next) = (new DateOnly(year, 1, 1), new DateOnly(year + 1, 1, 1));
        await db.MonthlyPlaytime
            .Where(m => m.LinkedAccountId == linkedAccountId && m.Month >= first && m.Month < next)
            .ExecuteDeleteAsync(ct);
        await db.YearReviews.Where(r => r.LinkedAccountId == linkedAccountId && r.Year == year).ExecuteDeleteAsync(ct);

        db.YearReviews.Add(new YearReviewRow { LinkedAccountId = linkedAccountId, Year = year, SnapshotId = snapshotId });
        db.MonthlyPlaytime.AddRange(months.Select(month => new MonthlyPlaytimeRow
        {
            LinkedAccountId = linkedAccountId,
            StoreEntryId = month.StoreEntryId,
            Month = month.Month,
            PlaytimeSeconds = (long)month.Playtime.TotalSeconds,
            Sessions = month.Sessions,
            SnapshotId = snapshotId,
        }));
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<(Guid LinkedAccountId, int Year)>> ListOwnerYearsAsync(Guid ownerId, CancellationToken ct) =>
        [
            .. (await (
                from review in db.YearReviews.AsNoTracking()
                join account in db.LinkedAccounts on review.LinkedAccountId equals account.Id
                where account.OwnerId == ownerId
                select new { review.LinkedAccountId, review.Year })
                .ToListAsync(ct))
                .Select(row => (row.LinkedAccountId, row.Year)),
        ];

    public async Task<IReadOnlyList<PlayedMonth>> ListPlayedAsync(Guid ownerId, CancellationToken ct)
    {
        var rows = await (
            from month in db.MonthlyPlaytime.AsNoTracking()
            join account in db.LinkedAccounts on month.LinkedAccountId equals account.Id
            where account.OwnerId == ownerId
            join entry in db.StoreEntries on month.StoreEntryId equals entry.Id
            select new { month.LinkedAccountId, month.Month, entry.GameId, month.PlaytimeSeconds, month.Sessions })
            .ToListAsync(ct);
        return [.. rows.Select(row => new PlayedMonth(row.LinkedAccountId, row.Month, row.GameId, TimeSpan.FromSeconds(row.PlaytimeSeconds), row.Sessions))];
    }
}

internal sealed class EfLibraryStore(GamesDbContext db) : ILibraryStore
{
    public async Task<IReadOnlyDictionary<Guid, Ownership>> FindOwnershipsAsync(Guid linkedAccountId, CancellationToken ct)
    {
        var rows = await db.Ownerships.AsNoTracking().Where(o => o.LinkedAccountId == linkedAccountId).ToListAsync(ct);
        return rows.ToDictionary(
            row => row.StoreEntryId,
            row => new Ownership(
                row.StoreEntryId,
                Playtime(row.PlaytimeSeconds),
                row.LastPlayedAt,
                row.FirstSeenAt,
                row.AsOf,
                row.SnapshotId,
                Playtime(row.RecentPlaytimeSeconds),
                row.UnlistedSince,
                row.MissingSince));
    }

    public async Task RecordAsync(
        Guid linkedAccountId,
        Guid snapshotId,
        IReadOnlyCollection<Observation> said,
        IReadOnlyCollection<Ownership> owned,
        CancellationToken ct)
    {
        db.Observations.AddRange(said.Select(observation => new ObservationRow
        {
            SnapshotId = snapshotId,
            StoreEntryId = observation.StoreEntryId,
            PlaytimeSeconds = Seconds(observation.Playtime),
            KnownPlaytimeSeconds = Seconds(observation.KnownPlaytime),
            RecentPlaytimeSeconds = Seconds(observation.RecentPlaytime),
            LastPlayedAt = observation.LastPlayedAt,
        }));

        Guid[] entries = [.. owned.Select(ownership => ownership.StoreEntryId)];
        var rows = await db.Ownerships
            .Where(o => o.LinkedAccountId == linkedAccountId && entries.Contains(o.StoreEntryId))
            .ToDictionaryAsync(o => o.StoreEntryId, ct);
        foreach (var ownership in owned)
        {
            if (!rows.TryGetValue(ownership.StoreEntryId, out var row))
            {
                row = new OwnershipRow { LinkedAccountId = linkedAccountId, StoreEntryId = ownership.StoreEntryId };
                db.Ownerships.Add(row);
            }

            row.PlaytimeSeconds = Seconds(ownership.Playtime);
            row.LastPlayedAt = ownership.LastPlayedAt;
            row.FirstSeenAt = ownership.FirstSeenAt;
            row.AsOf = ownership.AsOf;
            row.SnapshotId = ownership.SnapshotId;
            row.RecentPlaytimeSeconds = Seconds(ownership.RecentPlaytime);
            row.UnlistedSince = ownership.UnlistedSince;
            row.MissingSince = ownership.MissingSince;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OwnedEntry>> ListAsync(Guid ownerId, CancellationToken ct)
    {
        var rows = await (
            from ownership in db.Ownerships.AsNoTracking()
            join account in db.LinkedAccounts on ownership.LinkedAccountId equals account.Id
            where account.OwnerId == ownerId && ownership.UnlistedSince == null
            join entry in db.StoreEntries on ownership.StoreEntryId equals entry.Id
            join game in db.Games on entry.GameId equals game.Id
            select new
            {
                GameId = game.Id,
                GameName = game.Name,
                StoreEntryId = entry.Id,
                LinkedAccountId = account.Id,
                entry.Source,
                entry.ExternalId,
                ownership.PlaytimeSeconds,
                ownership.RecentPlaytimeSeconds,
                ownership.LastPlayedAt,
                ownership.FirstSeenAt,
                ownership.AsOf,
                ownership.MissingSince,
            })
            .ToListAsync(ct);
        return [.. rows.Select(row => new OwnedEntry(
            row.GameId,
            row.GameName,
            row.StoreEntryId,
            row.LinkedAccountId,
            row.Source,
            row.ExternalId,
            Playtime(row.PlaytimeSeconds),
            Playtime(row.RecentPlaytimeSeconds),
            row.LastPlayedAt,
            row.FirstSeenAt,
            row.AsOf,
            row.MissingSince))];
    }

    public async Task<IReadOnlyList<PlayedDay>> ListPlayedAsync(Guid ownerId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        // The differences are taken by the database, which reads the observations of the days asked
        // and no others: their snapshots come from the index on the linked account and the date, their
        // rows from the primary key. A reading that knew no total is skipped, so the next one is
        // compared with the last that did. Two readings of the same moment come in the order they
        // were recorded (a snapshot's id is made from the time).
        //
        // The first reading of a game in the days asked counts from the last one before them that knew
        // a total, however far back: the snapshots are walked backwards from the first day, one row
        // looked up in each, and the walk stops at the first that has one, or at the earliest reading
        // that listed the game: one bought in the days asked is looked up nowhere. That lookup is a
        // lateral join so that it is made once for a reading, and only for one with none before it.
        var since = (from ?? DateOnly.MinValue).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var until = to is { } day && day < DateOnly.MaxValue
            ? day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        var rows = await db.Database
            .SqlQuery<PlayedRow>(
                $"""
                WITH readings AS (
                    SELECT s.linked_account_id,
                           o.store_entry_id,
                           w.first_seen_at,
                           (s.taken_at AT TIME ZONE 'UTC')::date AS day,
                           o.known_playtime_seconds AS known,
                           LAG(o.known_playtime_seconds) OVER (
                               PARTITION BY s.linked_account_id, o.store_entry_id
                               ORDER BY s.taken_at, s.id) AS before
                    FROM games.linked_accounts a
                    JOIN games.raw_snapshots s ON s.linked_account_id = a.id
                    JOIN games.observations o ON o.snapshot_id = s.id
                    JOIN games.ownerships w ON w.linked_account_id = a.id AND w.store_entry_id = o.store_entry_id
                    WHERE a.owner_id = {ownerId}
                      AND s.taken_at >= {since} AND s.taken_at < {until}
                      AND w.unlisted_since IS NULL
                      AND o.known_playtime_seconds IS NOT NULL
                ),
                gains AS (
                    SELECT r.day,
                           r.store_entry_id,
                           r.known - COALESCE(r.before, earlier.known) AS gained
                    FROM readings r
                    LEFT JOIN LATERAL (
                        SELECT p.known_playtime_seconds AS known
                        FROM games.raw_snapshots ps
                        JOIN games.observations p ON p.snapshot_id = ps.id
                        WHERE r.before IS NULL
                          AND ps.linked_account_id = r.linked_account_id
                          AND ps.taken_at >= r.first_seen_at AND ps.taken_at < {since}
                          AND p.store_entry_id = r.store_entry_id
                          AND p.known_playtime_seconds IS NOT NULL
                        ORDER BY ps.taken_at DESC, ps.id DESC
                        LIMIT 1) earlier ON TRUE
                )
                SELECT g.day AS day, e.game_id AS game_id, SUM(g.gained)::bigint AS seconds
                FROM gains g
                JOIN games.store_entries e ON e.id = g.store_entry_id
                WHERE g.gained > 0
                GROUP BY g.day, e.game_id
                """)
            .ToListAsync(ct);
        return [.. rows.Select(row => new PlayedDay(row.Day, row.GameId, TimeSpan.FromSeconds(row.Seconds)))];
    }

    private static long? Seconds(TimeSpan? playtime) => playtime is { } value ? (long)value.TotalSeconds : null;

    private static TimeSpan? Playtime(long? seconds) => seconds is { } value ? TimeSpan.FromSeconds(value) : null;

    /// <summary>A row of <see cref="ListPlayedAsync"/>, as the query names its columns.</summary>
    private sealed class PlayedRow
    {
        public DateOnly Day { get; set; }

        public Guid GameId { get; set; }

        public long Seconds { get; set; }
    }
}
