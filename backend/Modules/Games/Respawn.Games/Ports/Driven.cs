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
    Task<IReadOnlyDictionary<string, StoreEntry>> FindStoreEntriesAsync(
        string source, IReadOnlyCollection<string> externalIds, CancellationToken ct);

    /// <summary>Adds store entries, each with the canonical record it points to. The record takes the
    /// store's name, and remembers that it did.</summary>
    Task AddAsync(IReadOnlyCollection<NewStoreEntry> entries, DateTimeOffset at, CancellationToken ct);

    /// <summary>Gives store entries the name their store now says: a store entry's name is the store's
    /// word, and always follows it. The canonical record follows only the entry it took its name from:
    /// a record named otherwise since (by hand, by a merge, from another source) keeps its name, and so
    /// does one whose other entries are renamed, even from the same name.</summary>
    Task RenameAsync(IReadOnlyCollection<StoreEntryRename> renames, CancellationToken ct);
}

public interface ILibraryStore
{
    /// <summary>What is known of a linked account's library, by store entry.</summary>
    Task<IReadOnlyDictionary<Guid, Ownership>> FindOwnershipsAsync(Guid linkedAccountId, CancellationToken ct);

    /// <summary>Keeps what a snapshot said, one row per game, and brings these ownerships to the state
    /// given. Ownerships that are not given are left as they were.</summary>
    Task RecordAsync(
        Guid linkedAccountId,
        Guid snapshotId,
        IReadOnlyCollection<Observation> said,
        IReadOnlyCollection<Ownership> owned,
        CancellationToken ct);

    /// <summary>Every way the owner owns a game today, in no order: the ownerships of their linked
    /// accounts, without those a complete reading left out since.</summary>
    Task<IReadOnlyList<OwnedEntry>> ListAsync(Guid ownerId, CancellationToken ct);
}

/// <summary>A store's libraries, read by a connector: an outbound adapter in a project of its own,
/// which knows the store and nothing of the catalogue.</summary>
public interface ILibrarySource
{
    /// <summary>The store it reads, as linked accounts name it (<see cref="LinkedAccount.Source"/>).</summary>
    string Source { get; }

    /// <summary>Reads the library of one account of the store, by the id the store gives that account.
    /// A library the store does not show (a profile made private) is a reading that lists nothing,
    /// not a failure.</summary>
    /// <exception cref="SourceException">The store could not be read.</exception>
    /// <exception cref="ArgumentException">Not an account id of this store.</exception>
    Task<LibraryReading> ReadLibraryAsync(string externalId, CancellationToken ct);
}
