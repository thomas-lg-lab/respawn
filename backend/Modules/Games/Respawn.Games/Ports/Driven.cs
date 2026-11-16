namespace Respawn.Games.Ports;

public interface IUnitOfWork
{
    /// <summary>Runs the work in one transaction, committed when it returns and rolled back when it
    /// throws. Writes to the catalogue run one at a time until the transaction ends, so two libraries
    /// recorded together cannot both add the same game. The work may run again from the start when the
    /// connection is lost on the way: it must only touch the database.</summary>
    Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken ct);
}

public interface ILinkedAccountStore
{
    Task<LinkedAccount?> FindAsync(string source, string externalId, CancellationToken ct);

    Task<LinkedAccount?> FindByIdAsync(Guid id, CancellationToken ct);

    Task AddAsync(LinkedAccount account, CancellationToken ct);
}

public interface ISnapshotStore
{
    Task AddAsync(RawSnapshot snapshot, CancellationToken ct);
}

public interface ICatalogue
{
    /// <summary>The store entries the catalogue already has among these, by their id in the store.</summary>
    Task<IReadOnlyDictionary<string, Guid>> FindStoreEntriesAsync(
        string source, IReadOnlyCollection<string> externalIds, CancellationToken ct);

    /// <summary>Adds store entries, each with the canonical record it points to.</summary>
    Task AddAsync(IReadOnlyCollection<NewStoreEntry> entries, DateTimeOffset at, CancellationToken ct);
}

public interface ILibraryStore
{
    /// <summary>Brings a linked account's ownership and playtime to what a snapshot says. Entries it
    /// does not list are left as they were.</summary>
    Task RecordAsync(
        Guid linkedAccountId, Guid snapshotId, IReadOnlyCollection<Ownership> owned, DateTimeOffset at, CancellationToken ct);

    /// <summary>Sorted by name.</summary>
    Task<IReadOnlyList<OwnedGame>> ListAsync(Guid ownerId, CancellationToken ct);
}
