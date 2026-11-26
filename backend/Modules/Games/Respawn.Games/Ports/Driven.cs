namespace Respawn.Games.Ports;

public interface IUnitOfWork
{
    /// <summary>Runs the work in one transaction, committed when it returns and rolled back when it
    /// throws. Writes to the catalogue run one at a time until the transaction ends, so two libraries
    /// recorded together cannot both add the same game. The work may run again from the start when the
    /// connection is lost on the way: it must only touch the database. Called from inside another
    /// run, the work joins that transaction.</summary>
    Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken ct);
}

public interface ILinkedAccountStore
{
    Task<LinkedAccount?> FindAsync(string source, string externalId, CancellationToken ct);

    Task<LinkedAccount?> FindByIdAsync(Guid id, CancellationToken ct);

    Task AddAsync(LinkedAccount account, CancellationToken ct);

    /// <summary>The store accounts linked to an owner, in no order.</summary>
    Task<IReadOnlyList<LinkedAccount>> ListAsync(Guid ownerId, CancellationToken ct);

    /// <summary>Every linked account of the instance, in no order.</summary>
    Task<IReadOnlyList<LinkedAccount>> ListAllAsync(CancellationToken ct);

    /// <summary>Removes a linked account with everything that was read from it: its ownerships, its
    /// snapshots and what they said, and its syncs. The catalogue stays: a game and its store entries are the
    /// instance's, not the owner's.</summary>
    Task RemoveAsync(Guid id, CancellationToken ct);
}

public interface ISnapshotStore
{
    Task AddAsync(RawSnapshot snapshot, CancellationToken ct);
}

public interface ISyncRunStore
{
    Task AddAsync(SyncRun run, CancellationToken ct);

    /// <summary>The runs started at that moment or later, of every linked account, in no order.</summary>
    Task<IReadOnlyList<SyncRun>> ListSinceAsync(DateTimeOffset since, CancellationToken ct);

    /// <summary>The run of a linked account that started last, if it ever had one.</summary>
    Task<SyncRun?> FindLatestAsync(Guid linkedAccountId, CancellationToken ct);

    /// <summary>The last run and the last one that succeeded, the latest started first, of each of these
    /// linked accounts, in one reading. An account that is not there any more is left out.</summary>
    Task<IReadOnlyDictionary<Guid, LatestRuns>> FindLatestAsync(IReadOnlyCollection<Guid> linkedAccountIds, CancellationToken ct);
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

    /// <summary>The store entries of a game, in no order, each with where its store says its cover is.
    /// Null when the catalogue has no such game.</summary>
    Task<IReadOnlyList<CoverEntry>?> FindCoverEntriesAsync(Guid gameId, CancellationToken ct);

    /// <summary>The store entries a linked account owns whose store was never asked where their cover
    /// is, or was asked before <paramref name="askedBefore"/>, in no order.</summary>
    Task<IReadOnlyList<CoverEntry>> ListCoverPathsDueAsync(Guid linkedAccountId, DateTimeOffset askedBefore, CancellationToken ct);

    /// <summary>Keeps where a store says these entries' covers are, none included, and that it was
    /// asked at that moment.</summary>
    Task SetCoverPathsAsync(IReadOnlyCollection<CoverPath> paths, DateTimeOffset at, CancellationToken ct);
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
    /// accounts, those noted as missing included, without those that left the library.</summary>
    Task<IReadOnlyList<OwnedEntry>> ListAsync(Guid ownerId, CancellationToken ct);

    /// <summary>What the owner played, per day and per game, in no order, over the same ownerships as
    /// <see cref="ListAsync"/>. For each of them, the readings that knew a total
    /// (<see cref="Observation.KnownPlaytime"/>) are taken in the order of their dates: a total greater
    /// than the one before is played time, dated by the reading that shows it; the first one is where
    /// the count starts and gives nothing, and a total that went down gives nothing either. Only the
    /// days from <paramref name="from"/> to <paramref name="to"/>, both included, when they are given;
    /// a growth is still counted from the reading before, whatever its day.</summary>
    Task<IReadOnlyList<PlayedDay>> ListPlayedAsync(Guid ownerId, DateOnly? from, DateOnly? to, CancellationToken ct);
}

public interface IYearReviewStore
{
    /// <summary>The years of a linked account whose review is kept.</summary>
    Task<IReadOnlySet<int>> ListYearsAsync(Guid linkedAccountId, CancellationToken ct);

    /// <summary>Keeps the review of a year: which snapshot it is, and what it says per month and per game.
    /// A year kept already is replaced whole.</summary>
    Task RecordAsync(Guid linkedAccountId, int year, Guid snapshotId, IReadOnlyCollection<MonthPlayed> months, CancellationToken ct);

    /// <summary>The years kept for each of an owner's linked accounts.</summary>
    Task<IReadOnlyList<(Guid LinkedAccountId, int Year)>> ListOwnerYearsAsync(Guid ownerId, CancellationToken ct);

    /// <summary>What an owner played per month and per game, from every review kept of their linked
    /// accounts, in no order. A game they no longer own is in it: a review counts what was played.</summary>
    Task<IReadOnlyList<PlayedMonth>> ListPlayedAsync(Guid ownerId, CancellationToken ct);
}

/// <summary>A store's review of a year of an account, read by a connector: what it played per month.</summary>
public interface IYearReviewSource
{
    /// <summary>The store it reads, as linked accounts name it (<see cref="LinkedAccount.Source"/>).</summary>
    string Source { get; }

    /// <summary>Reads the review of one year. A year the store has no review of is a reading that says
    /// so (<see cref="YearReading.Exists"/>), not a failure.</summary>
    /// <param name="externalId">The account, as the store names it.</param>
    /// <param name="year">The year.</param>
    /// <param name="credential">The owner's own access, when they gave one; the instance's otherwise.
    /// One given is the only one tried, as for <see cref="ILibrarySource"/>.</param>
    /// <param name="ct">Gives up.</param>
    /// <exception cref="SourceException">The store could not be read.</exception>
    /// <exception cref="ArgumentException">Not an account id of this store.</exception>
    Task<YearReading> ReadYearAsync(string externalId, int year, string? credential, CancellationToken ct);
}

/// <summary>A store's libraries, read by a connector: an outbound adapter in a project of its own,
/// which knows the store and nothing of the catalogue.</summary>
public interface ILibrarySource
{
    /// <summary>The store it reads, as linked accounts name it (<see cref="LinkedAccount.Source"/>).</summary>
    string Source { get; }

    /// <summary>The window the store counts recent playtime over (<see cref="OwnedItem.RecentPlaytime"/>;
    /// Steam: two weeks). Once a reading is older than that, nothing of what it said was played lately
    /// is recent any more.</summary>
    TimeSpan RecentWindow { get; }

    /// <summary>Reads the library of one account of the store, by the id the store gives that account.
    /// A library the store does not show (a profile made private) is a reading that lists nothing,
    /// not a failure.</summary>
    /// <param name="externalId">The account, as the store names it.</param>
    /// <param name="credential">The owner's own access to the store (a key, a token), when they gave
    /// one (<see cref="ICredentialStore"/>); null to read with the instance's. One given is the only
    /// one tried: a store that refuses it is a failure, never a reading with the instance's.</param>
    /// <param name="ct">Gives up.</param>
    /// <exception cref="SourceException">The store could not be read.</exception>
    /// <exception cref="ArgumentException">Not an account id of this store.</exception>
    Task<LibraryReading> ReadLibraryAsync(string externalId, string? credential, CancellationToken ct);

    /// <summary>Whether this can be an owner's access to the store, by its shape alone: what cannot is
    /// refused before it is kept, and never sent to the store.</summary>
    bool IsCredential(string credential);
}

/// <summary>A store's covers, read by a connector. Nothing here needs an access to the store: a cover
/// is public, and is asked for without the instance's key or an owner's.</summary>
public interface ICoverSource
{
    /// <summary>The store it reads, as store entries name it.</summary>
    string Source { get; }

    /// <summary>The most games the store says the covers' places of in one answer.</summary>
    int PathsPerRequest { get; }

    /// <summary>The cover of one game of the store, or null when the store has none.</summary>
    /// <param name="externalId">The game's id in the store.</param>
    /// <param name="path">Where the store said the cover is (<see cref="ReadCoverPathsAsync"/>), when
    /// it was asked and said; the connector then looks there first.</param>
    /// <param name="ct">Gives up.</param>
    /// <exception cref="SourceException">The store could not be read.</exception>
    Task<Cover?> ReadCoverAsync(string externalId, string? path, CancellationToken ct);

    /// <summary>Where the store says the covers of these games are, by the game's id in the store: a
    /// short text the connector understands, never an image nor a whole address. A game the store does
    /// not know, or knows no cover of, is left out.</summary>
    /// <param name="externalIds">At most <see cref="PathsPerRequest"/> ids.</param>
    /// <param name="ct">Gives up.</param>
    /// <exception cref="SourceException">The store could not be read.</exception>
    Task<IReadOnlyDictionary<string, string>> ReadCoverPathsAsync(IReadOnlyCollection<string> externalIds, CancellationToken ct);
}
