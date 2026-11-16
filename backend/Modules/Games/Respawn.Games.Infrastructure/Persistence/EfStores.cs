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
            OwnerId = snapshot.OwnerId,
            LinkedAccountId = snapshot.LinkedAccountId,
            Payload = snapshot.Payload,
        });
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class EfCatalogue(GamesDbContext db) : ICatalogue
{
    public async Task<IReadOnlyDictionary<string, Guid>> FindStoreEntriesAsync(
        string source, IReadOnlyCollection<string> externalIds, CancellationToken ct)
    {
        string[] ids = [.. externalIds];
        return await db.StoreEntries.AsNoTracking()
            .Where(e => e.Source == source && ids.Contains(e.ExternalId))
            .ToDictionaryAsync(e => e.ExternalId, e => e.Id, StringComparer.Ordinal, ct);
    }

    public async Task AddAsync(IReadOnlyCollection<NewStoreEntry> entries, DateTimeOffset at, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            db.Games.Add(new GameRow { Id = entry.GameId, Name = entry.Name, CreatedAt = at });
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
}

internal sealed class EfLibraryStore(GamesDbContext db) : ILibraryStore
{
    public async Task RecordAsync(
        Guid linkedAccountId, Guid snapshotId, IReadOnlyCollection<Ownership> owned, DateTimeOffset at, CancellationToken ct)
    {
        var known = await db.Ownerships
            .Where(o => o.LinkedAccountId == linkedAccountId)
            .ToDictionaryAsync(o => o.StoreEntryId, ct);
        foreach (var ownership in owned)
        {
            if (!known.TryGetValue(ownership.StoreEntryId, out var row))
            {
                row = new OwnershipRow { LinkedAccountId = linkedAccountId, StoreEntryId = ownership.StoreEntryId, FirstSeenAt = at };
                db.Ownerships.Add(row);
            }

            row.PlaytimeSeconds = (long)ownership.Playtime.TotalSeconds;
            row.LastPlayedAt = ownership.LastPlayedAt;
            row.SnapshotId = snapshotId;
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
            orderby game.Name, entry.Source, entry.ExternalId, account.Id
            select new { game.Id, game.Name, entry.Source, entry.ExternalId, ownership.PlaytimeSeconds, ownership.LastPlayedAt })
            .ToListAsync(ct);
        return [.. rows.Select(row => new OwnedGame(
            row.Id, row.Name, row.Source, row.ExternalId, TimeSpan.FromSeconds(row.PlaytimeSeconds), row.LastPlayedAt))];
    }
}
