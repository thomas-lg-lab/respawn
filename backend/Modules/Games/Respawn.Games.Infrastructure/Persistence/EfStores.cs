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
            OwnerId = snapshot.OwnerId,
            LinkedAccountId = snapshot.LinkedAccountId,
            Payload = snapshot.Payload,
        });
        await db.SaveChangesAsync(ct);
    }
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
            db.Games.Add(new GameRow { Id = entry.GameId, Name = entry.Name, NameFromStore = true, CreatedAt = at });
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
        var games = await db.Games.Where(g => gameIds.Contains(g.Id) && g.NameFromStore).ToDictionaryAsync(g => g.Id, ct);
        foreach (var rename in renames)
        {
            var entry = entries[rename.Id];
            entry.Name = rename.Name;
            // The record follows while its name is still this entry's previous one. After a merge, the
            // record keeps the name of the entry it was created with, whatever the others become.
            if (games.TryGetValue(entry.GameId, out var game) && game.Name == rename.Was)
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
            row => new Ownership(row.StoreEntryId, Playtime(row.PlaytimeSeconds), row.LastPlayedAt, row.FirstSeenAt, row.AsOf, row.SnapshotId));
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
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OwnedGame>> ListAsync(Guid ownerId, CancellationToken ct)
    {
        var rows = await (
            from ownership in db.Ownerships.AsNoTracking()
            join account in db.LinkedAccounts on ownership.LinkedAccountId equals account.Id
            where account.OwnerId == ownerId
            join entry in db.StoreEntries on ownership.StoreEntryId equals entry.Id
            join game in db.Games on entry.GameId equals game.Id
            orderby game.Name == null, game.Name, entry.Source, entry.ExternalId, account.Id
            select new { game.Id, game.Name, entry.Source, entry.ExternalId, ownership.PlaytimeSeconds, ownership.LastPlayedAt })
            .ToListAsync(ct);
        return [.. rows.Select(row => new OwnedGame(
            row.Id, row.Name, row.Source, row.ExternalId, Playtime(row.PlaytimeSeconds), row.LastPlayedAt))];
    }

    private static long? Seconds(TimeSpan? playtime) => playtime is { } value ? (long)value.TotalSeconds : null;

    private static TimeSpan? Playtime(long? seconds) => seconds is { } value ? TimeSpan.FromSeconds(value) : null;
}
