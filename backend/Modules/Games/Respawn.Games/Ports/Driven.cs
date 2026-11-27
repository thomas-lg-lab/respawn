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

    /// <summary>A game with its store entries, in no order. Null when the catalogue has no such game.</summary>
    Task<CatalogueGame?> FindGameAsync(Guid gameId, CancellationToken ct);

    /// <summary>The names of these games, by game; null for one no source has named. A game the
    /// catalogue does not have is left out.</summary>
    Task<IReadOnlyDictionary<Guid, string?>> FindNamesAsync(IReadOnlyCollection<Guid> gameIds, CancellationToken ct);

    /// <summary>The store entries of a game, in no order, each with where its store says its cover is.
    /// Null when the catalogue has no such game.</summary>
    Task<IReadOnlyList<CoverEntry>?> FindCoverEntriesAsync(Guid gameId, CancellationToken ct);

    /// <summary>The linked accounts that own at least one store entry whose store was never asked
    /// where its cover is, or was asked before <paramref name="askedBefore"/>, in no order.</summary>
    Task<IReadOnlyList<Guid>> ListAccountsWithCoverPathsDueAsync(DateTimeOffset askedBefore, CancellationToken ct);

    /// <summary>The store entries a linked account owns whose store was never asked where their cover
    /// is, or was asked before <paramref name="askedBefore"/>, in no order.</summary>
    Task<IReadOnlyList<CoverEntry>> ListCoverPathsDueAsync(Guid linkedAccountId, DateTimeOffset askedBefore, CancellationToken ct);

    /// <summary>Keeps where a store says these entries' covers are, none included, and that it was
    /// asked at that moment.</summary>
    Task SetCoverPathsAsync(IReadOnlyCollection<CoverPath> paths, DateTimeOffset at, CancellationToken ct);

    /// <summary>The image a game shows for a kind in a slot (<see cref="ImageSlots"/>), when one is kept.</summary>
    Task<KeptImage?> FindImageAsync(Guid gameId, string kind, string slot, CancellationToken ct);

    /// <summary>Makes this the image the game shows for its kind in a slot, in the place of the one it
    /// showed there. Returns that one when it was another, whose file may go once no game shows it in
    /// any slot (<see cref="IsImageShownAsync"/>); null otherwise.</summary>
    Task<ImageName?> KeepImageAsync(Guid gameId, string slot, KeptImage image, CancellationToken ct);

    /// <summary>Whether a game shows this image, in any slot: its file is removed only once none does.</summary>
    Task<bool> IsImageShownAsync(ImageName image, CancellationToken ct);

    /// <summary>Says the source of the image a game shows for a kind in a slot was asked for it at that
    /// moment, and gave nothing newer.</summary>
    Task CheckedImageAsync(Guid gameId, string kind, string slot, DateTimeOffset at, CancellationToken ct);

    /// <summary>The cover the game database gives a game (<see cref="GameDataCover"/>).</summary>
    Task<GameDataCover> FindGameDataCoverAsync(Guid gameId, CancellationToken ct);

    /// <summary>What each of these games has a cover from (<see cref="CoverOrigins"/>); a game with no
    /// store entry is left out.</summary>
    Task<IReadOnlyDictionary<Guid, CoverOrigins>> FindCoverOriginsAsync(IReadOnlyCollection<Guid> gameIds, CancellationToken ct);

    /// <summary>Whether the catalogue holds anything the game database said of a game.</summary>
    Task<bool> HasGameDataAsync(CancellationToken ct);

    /// <summary>The stores, among these, that have a store entry the game database is to be asked
    /// about: never asked, or asked before the moment <paramref name="due"/> says for what it answered.</summary>
    Task<IReadOnlyList<string>> ListStoresWithGameDataDueAsync(IReadOnlyCollection<string> stores, GameDataDue due, CancellationToken ct);

    /// <summary>At most <paramref name="max"/> store entries of a store the game database is to be asked
    /// about: those never asked first, then those asked the longest ago.</summary>
    Task<IReadOnlyList<GameDataEntry>> ListGameDataDueAsync(string store, GameDataDue due, int max, CancellationToken ct);

    /// <summary>Keeps what the game database said of its games, one record per game for the whole
    /// instance, in the place of what it said before; then which of its games these store entries are,
    /// none included, and that it was asked at that moment. A game of the database no store entry is any
    /// more is forgotten.</summary>
    Task SetGameDataAsync(IReadOnlyCollection<GameFacts> games, IReadOnlyCollection<GameDataMatch> matches, DateTimeOffset at, CancellationToken ct);
}

/// <summary>Where the images an instance keeps are written: files of a data directory. An instance
/// given none has <see cref="NoImageStore"/>, which keeps nothing, and its covers are passed on from
/// their source at each request.</summary>
public interface IImageStore
{
    /// <summary>Whether images are kept at all.</summary>
    bool Keeps { get; }

    /// <summary>The kept image, to read and dispose of; null when there is no such file.</summary>
    Task<Stream?> OpenAsync(ImageName image, CancellationToken ct);

    /// <summary>Writes the image whole, or not at all: a reader never finds half of one. False when it
    /// could not be written (a full disk, a directory gone): the store says so in the log, and the
    /// image is passed on without being kept.</summary>
    Task<bool> WriteAsync(ImageName image, ReadOnlyMemory<byte> bytes, CancellationToken ct);

    /// <summary>Removes the image's file; nothing when there is none.</summary>
    void Delete(ImageName image);
}

/// <summary>An instance with no data directory: nothing is kept.</summary>
public sealed class NoImageStore : IImageStore
{
    public bool Keeps => false;

    public Task<Stream?> OpenAsync(ImageName image, CancellationToken ct) => Task.FromResult<Stream?>(null);

    public Task<bool> WriteAsync(ImageName image, ReadOnlyMemory<byte> bytes, CancellationToken ct) => Task.FromResult(false);

    public void Delete(ImageName image)
    {
    }
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
    /// accounts, those noted as missing included, without those that left the library. Each with the
    /// source of the cover the owner chose for its game.</summary>
    Task<IReadOnlyList<OwnedEntry>> ListAsync(Guid ownerId, CancellationToken ct);

    /// <summary>The source of the cover the owner chose for a game; null while they chose none.</summary>
    Task<string?> FindCoverChoiceAsync(Guid ownerId, Guid gameId, CancellationToken ct);

    /// <summary>Keeps the owner's choice of a cover for a game, made at that moment, in the place of the
    /// one they made before; null forgets it.</summary>
    Task SetCoverChoiceAsync(Guid ownerId, Guid gameId, string? source, DateTimeOffset at, CancellationToken ct);

    /// <summary>What is known of a game through each of an owner's linked accounts, in the library or
    /// out of it, in no order. Empty when they never owned it.</summary>
    Task<IReadOnlyList<AccountOwnership>> FindGameOwnershipsAsync(Guid ownerId, Guid gameId, CancellationToken ct);

    /// <summary>Brings these ownerships, which exist, to the state given.</summary>
    Task SaveAsync(IReadOnlyCollection<AccountOwnership> owned, CancellationToken ct);

    /// <summary>The ownerships the owner took out of their library, in no order. Only those of one game
    /// when <paramref name="gameId"/> is given.</summary>
    Task<IReadOnlyList<RemovedEntry>> ListRemovedAsync(Guid ownerId, Guid? gameId, CancellationToken ct);

    /// <summary>When the first of the readings that listed one of these ownerships was taken: the
    /// earliest <see cref="OwnedEntry.FirstSeenAt"/> of <see cref="ListAsync"/>, without reading the
    /// library. Null while the owner owns nothing.</summary>
    Task<DateTimeOffset?> FindFirstSeenAsync(Guid ownerId, CancellationToken ct);

    /// <summary>What the owner played, per day and per game, in no order, over the same ownerships as
    /// <see cref="ListAsync"/>. For each of them, the readings that knew a total
    /// (<see cref="Observation.KnownPlaytime"/>) are taken in the order of their dates: a total greater
    /// than the one before is played time, dated by the reading that shows it; the first one is where
    /// the count starts and gives nothing, and a total that went down gives nothing either. Only the
    /// days from <paramref name="from"/> to <paramref name="to"/>, both included, when they are given;
    /// a growth is still counted from the reading before, whatever its day. Only one game when
    /// <paramref name="gameId"/> is given, and then only its readings are read.</summary>
    Task<IReadOnlyList<PlayedDay>> ListPlayedAsync(Guid ownerId, Guid? gameId, DateOnly? from, DateOnly? to, CancellationToken ct);
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
    /// accounts, in no order. A game they no longer own is in it: a review counts what was played.
    /// Only one game when <paramref name="gameId"/> is given, and only the month
    /// <paramref name="month"/> is the first day of when it is: the other rows are not read.</summary>
    Task<IReadOnlyList<PlayedMonth>> ListPlayedAsync(Guid ownerId, Guid? gameId, DateOnly? month, CancellationToken ct);
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

/// <summary>A game database, read by a connector: what it knows of the games of the stores. It is asked
/// with the instance's access alone, never an owner's; an instance given none runs without it.</summary>
public interface IGameDataSource
{
    /// <summary>The database, as the catalogue names it.</summary>
    string Source { get; }

    /// <summary>The stores whose games it is asked about, as store entries name them.</summary>
    IReadOnlyCollection<string> Stores { get; }

    /// <summary>Whether the instance has an access to it now: when not, nothing is asked. Read again each
    /// time: an access may be given after the app started.</summary>
    bool HasAccess { get; }

    /// <summary>The most games of a store asked about at once.</summary>
    int MatchesPerRequest { get; }

    /// <summary>Its name, as people know it, for the credit its terms ask for.</summary>
    string Label { get; }

    /// <summary>Its home page, which the credit leads to.</summary>
    Uri Home { get; }

    /// <summary>A cover from its image host, by the id its games give it (<see cref="GameFacts.CoverImageId"/>),
    /// or null when the host has no such image. Nothing here needs the instance's access: its images are
    /// public.</summary>
    /// <exception cref="SourceException">The image host could not be read.</exception>
    Task<Cover?> ReadCoverAsync(string imageId, CancellationToken ct);

    /// <summary>The most of its own games asked about at once (<see cref="ReadGamesAsync"/>).</summary>
    int GamesPerRequest { get; }

    /// <summary>What it knows of some of its games: names, genres, themes, a summary, a cover and how
    /// long they take. An id that cannot be one of its games is not sent; when none is left, nothing is
    /// asked.</summary>
    /// <param name="gameIds">At most <see cref="GamesPerRequest"/> ids of its games, as matches give them.</param>
    /// <param name="ct">Gives up.</param>
    /// <exception cref="SourceException">The database could not be read, or the instance has no access
    /// to it (<see cref="SourceError.NotConfigured"/>).</exception>
    /// <exception cref="ArgumentException">Too many ids.</exception>
    Task<GamesReading> ReadGamesAsync(IReadOnlyCollection<string> gameIds, CancellationToken ct);

    /// <summary>Which games of the database these games of a store are. An id that cannot be a game of
    /// the store is not sent; when none is left, nothing is asked.</summary>
    /// <param name="store">The store, as store entries name it.</param>
    /// <param name="externalIds">At most <see cref="MatchesPerRequest"/> ids of games in the store.</param>
    /// <param name="ct">Gives up.</param>
    /// <exception cref="SourceException">The database could not be read, or the instance has no access
    /// to it (<see cref="SourceError.NotConfigured"/>).</exception>
    /// <exception cref="ArgumentException">A store the database is not asked about, or too many ids.</exception>
    Task<MatchReading> MatchAsync(string store, IReadOnlyCollection<string> externalIds, CancellationToken ct);
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
    /// short text the connector understands, never an image nor a whole address; null for a game the
    /// store answers about and knows no cover of, or does not know, and for an id that cannot be a
    /// game of the store, which is not sent. A game the answer says nothing about is left out.</summary>
    /// <param name="externalIds">At most <see cref="PathsPerRequest"/> ids.</param>
    /// <param name="ct">Gives up.</param>
    /// <exception cref="SourceException">The store could not be read.</exception>
    Task<IReadOnlyDictionary<string, string?>> ReadCoverPathsAsync(IReadOnlyCollection<string> externalIds, CancellationToken ct);
}
