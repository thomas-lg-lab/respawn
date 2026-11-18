using Microsoft.EntityFrameworkCore;
using Respawn.Games.Ports;

namespace Respawn.Games.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(GamesDbContext db) : IUnitOfWork
{
    /// <summary>The advisory lock every write to the catalogue takes; any number, as long as it is
    /// always this one and no other module's.</summary>
    private const long CatalogueLock = 7306;

    public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken ct) =>
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
        ToLinkedAccount(await db.LinkedAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Source == source && a.ExternalId == externalId, ct));

    public async Task<LinkedAccount?> FindByIdAsync(Guid id, CancellationToken ct) =>
        ToLinkedAccount(await db.LinkedAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct));

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
        [.. (await db.LinkedAccounts.AsNoTracking().Where(a => a.OwnerId == ownerId).ToListAsync(ct)).Select(row => ToLinkedAccount(row)!)];

    public async Task<IReadOnlyList<LinkedAccount>> ListAllAsync(CancellationToken ct) =>
        [.. (await db.LinkedAccounts.AsNoTracking().ToListAsync(ct)).Select(row => ToLinkedAccount(row)!)];

    public async Task RemoveAsync(Guid id, CancellationToken ct)
    {
        // In the order the foreign keys allow: what points to a snapshot before the snapshots, and
        // the account last. Nothing here is deleted along with something else by the database.
        await db.SyncRuns.Where(r => r.LinkedAccountId == id).ExecuteDeleteAsync(ct);
        await db.Ownerships.Where(o => o.LinkedAccountId == id).ExecuteDeleteAsync(ct);
        await db.Observations
            .Where(o => db.RawSnapshots.Any(s => s.Id == o.SnapshotId && s.LinkedAccountId == id))
            .ExecuteDeleteAsync(ct);
        await db.RawSnapshots.Where(s => s.LinkedAccountId == id).ExecuteDeleteAsync(ct);
        await db.LinkedAccounts.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
    }

    private static LinkedAccount? ToLinkedAccount(LinkedAccountRow? row) =>
        row is null ? null : new LinkedAccount(row.Id, row.OwnerId, row.Source, row.ExternalId, row.DisplayName, row.LinkedAt);
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
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SyncRun>> ListSinceAsync(DateTimeOffset since, CancellationToken ct) =>
        [.. (await db.SyncRuns.AsNoTracking().Where(r => r.StartedAt >= since).ToListAsync(ct)).Select(ToRun)];

    public async Task<SyncRun?> FindLatestAsync(Guid linkedAccountId, CancellationToken ct) =>
        await db.SyncRuns.AsNoTracking()
            .Where(r => r.LinkedAccountId == linkedAccountId)
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
            row.SnapshotId);
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
            row.AsOf))];
    }

    private static long? Seconds(TimeSpan? playtime) => playtime is { } value ? (long)value.TotalSeconds : null;

    private static TimeSpan? Playtime(long? seconds) => seconds is { } value ? TimeSpan.FromSeconds(value) : null;
}
