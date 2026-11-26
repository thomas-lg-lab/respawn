namespace Respawn.Games;

// The words of the module. A game is one canonical record whatever the store; a store entry is that
// game as one store knows it, and points to one canonical record. An owner is an account of the
// instance, known here by its id only.

/// <summary>A store account proven to belong to an account of the instance. <paramref name="HasCredential"/>
/// says whether its owner gave their own access to the store (a key), never what it is.</summary>
public sealed record LinkedAccount(
    Guid Id,
    Guid OwnerId,
    string Source,
    string ExternalId,
    string? DisplayName,
    DateTimeOffset LinkedAt,
    bool HasCredential = false);

/// <summary>What a source answered, kept as it came and dated: a field can be read from it again later
/// without asking the source. Personal when it was read from a linked account (a library, its owner's),
/// the instance's otherwise.</summary>
/// <param name="Id">The snapshot.</param>
/// <param name="Source">The source that answered.</param>
/// <param name="Kind">Which of the source's answers this is.</param>
/// <param name="TakenAt">When the source answered: the date the answer is true for.</param>
/// <param name="RecordedAt">When it was stored here, which may be later: a retry, an import of the past.</param>
/// <param name="Complete">True when it is taken as the whole of what it lists: what it leaves out is
/// then noted as missing. See <see cref="LibraryReading"/>.</param>
/// <param name="LinkedAccountId">The store account it was read from, when personal.</param>
/// <param name="Payload">The answer as received.</param>
public sealed record RawSnapshot(
    Guid Id,
    string Source,
    string Kind,
    DateTimeOffset TakenAt,
    DateTimeOffset RecordedAt,
    Guid? LinkedAccountId,
    string Payload,
    bool Complete = false);

/// <summary>A store entry the catalogue has, with the name its store last gave it, if any.</summary>
public sealed record StoreEntry(Guid Id, string? Name);

/// <summary>A game a store knows and the catalogue did not: its store entry, and the canonical record
/// created for it, under the store's name.</summary>
public sealed record NewStoreEntry(Guid Id, Guid GameId, string Source, string ExternalId, string? Name);

/// <summary>A store entry whose store now gives another name than the one kept.</summary>
public sealed record StoreEntryRename(Guid Id, string Name);

/// <summary>A library as a connector read it: the source's answer untouched, and what it says.</summary>
/// <param name="Kind">Which of the source's answers this is, in the connector's words.</param>
/// <param name="TakenAt">When the source answered, not when the reading is handed over.</param>
/// <param name="Payload">The answer as received.</param>
/// <param name="Items">The games it lists.</param>
/// <param name="Complete">True when the connector vouches that this is the whole library: the source
/// answered in full, nothing was cut or filtered. A game such a reading does not list is noted as
/// missing (<see cref="Ownership"/>), and stays in the library. A reading that lists nothing is never
/// taken as complete, whatever this says: a profile made private answers an empty list that looks the
/// same.</param>
public sealed record LibraryReading(
    string Kind, DateTimeOffset TakenAt, string Payload, IReadOnlyList<OwnedItem> Items, bool Complete = false);

/// <summary>A game of a library, in the store's words. What the store does not say is null, never a
/// value standing for it: an answer may be poorer than the one before.</summary>
/// <param name="ExternalId">The game's id in the store.</param>
/// <param name="Name">Null when the store gives none.</param>
/// <param name="Playtime">The total played, as the store counts it; null when it does not say.</param>
/// <param name="LastPlayedAt">Null when the store does not say, or the game was never launched.</param>
/// <param name="RecentPlaytime">What was played lately, over the window the store itself counts (Steam:
/// the last two weeks); null when it does not say. It is the store's own figure: nothing here can
/// compute it before two weeks of readings exist.</param>
public sealed record OwnedItem(
    string ExternalId, string? Name, TimeSpan? Playtime, DateTimeOffset? LastPlayedAt, TimeSpan? RecentPlaytime = null);

/// <summary>What one snapshot said of one store entry: the history of a library.</summary>
/// <param name="StoreEntryId">The store entry.</param>
/// <param name="Playtime">The total played, as the snapshot said it: null or zero when it was poor.</param>
/// <param name="LastPlayedAt">As the snapshot said it.</param>
/// <param name="KnownPlaytime">The total played as it was known once this snapshot was read: what it
/// said, or what was known before when it did not say. A history is a difference between these, never
/// between what was said: a poor answer between two good ones would count the same hours twice. Null
/// while no total is known, which a zero does not always end: a reading that gives no time to any game
/// is a store hiding the playtime, and its zeros are not totals to count from.</param>
/// <param name="RecentPlaytime">What was played lately, as the snapshot said it.</param>
public sealed record Observation(
    Guid StoreEntryId, TimeSpan? Playtime, DateTimeOffset? LastPlayedAt, TimeSpan? KnownPlaytime, TimeSpan? RecentPlaytime);

/// <summary>A store entry a linked account owns: the best that is known of it, from every reading so
/// far.</summary>
/// <param name="StoreEntryId">The store entry.</param>
/// <param name="Playtime">The total played; null while no reading said.</param>
/// <param name="LastPlayedAt">Null while no reading said.</param>
/// <param name="FirstSeenAt">The date of the earliest reading that listed it.</param>
/// <param name="AsOf">The date of the latest reading that listed it.</param>
/// <param name="SnapshotId">That latest reading's snapshot.</param>
/// <param name="RecentPlaytime">What was played lately, as that latest reading said it. A window that
/// moves cannot be carried over: when the reading does not say, it is null again.</param>
/// <param name="UnlistedSince">The date from which it is out of the library; null while it is in it.
/// No reading sets it: a game leaves a library when its owner says so. What was known of it and its
/// history stay; a later reading that lists it brings it back.</param>
/// <param name="MissingSince">The date of the earliest complete reading that left it out, while none
/// has listed it since (a refund, a share that ended, a free weekend over, or an answer cut short that
/// still agrees with itself: nothing here can tell which). The game stays in the library, flagged;
/// null once a reading lists it again.</param>
public sealed record Ownership(
    Guid StoreEntryId,
    TimeSpan? Playtime,
    DateTimeOffset? LastPlayedAt,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset AsOf,
    Guid SnapshotId,
    TimeSpan? RecentPlaytime = null,
    DateTimeOffset? UnlistedSince = null,
    DateTimeOffset? MissingSince = null);

/// <summary>One way an owner owns a game: a store entry, through one of their linked accounts. The pair
/// of the two ids is unique in a library.</summary>
public sealed record OwnedEntry(
    Guid GameId,
    string? GameName,
    Guid StoreEntryId,
    Guid LinkedAccountId,
    string Source,
    string ExternalId,
    TimeSpan? Playtime,
    TimeSpan? RecentPlaytime,
    DateTimeOffset? LastPlayedAt,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset AsOf,
    DateTimeOffset? MissingSince = null);

/// <summary>A game of an owner's library, once, however many store entries it is owned through: after
/// a merge, or with two accounts of one store, a game has several.</summary>
/// <param name="GameId">The canonical record.</param>
/// <param name="Name">Null while no source has named the game.</param>
/// <param name="Playtime">The sum over its entries; null while none said.</param>
/// <param name="RecentPlaytime">The sum over its entries whose reading is younger than its store's
/// window; null while none said.</param>
/// <param name="LastPlayedAt">The latest over its entries.</param>
/// <param name="Entries">The ways it is owned, at least one.</param>
public sealed record OwnedGame(
    Guid GameId,
    string? Name,
    TimeSpan? Playtime,
    TimeSpan? RecentPlaytime,
    DateTimeOffset? LastPlayedAt,
    IReadOnlyList<OwnedEntry> Entries);

/// <summary>The orders a library is read in. Whatever the order, the games it cannot place (no name,
/// no playtime said, never launched) come last.</summary>
public enum LibrarySort
{
    /// <summary>By name, A to Z.</summary>
    Name,

    /// <summary>The most played first.</summary>
    Playtime,

    /// <summary>The last played first.</summary>
    LastPlayed,
}

/// <summary>A part of a library, and how many games the whole of it has.</summary>
public sealed record LibraryPage(int Total, IReadOnlyList<OwnedGame> Games);

/// <summary>A library in figures, as the stores' totals give them: what needs a history (a year, a
/// streak) is not here. Counted over the same games as <see cref="LibraryPage"/>, those flagged as
/// missing included.</summary>
/// <param name="Games">The games, each once however many entries it is owned through.</param>
/// <param name="Launched">The games a store says were played: some playtime, or a last session.</param>
/// <param name="NeverLaunched">The others: no store says they were ever played.</param>
/// <param name="Missing">The games every entry of which is flagged as missing.</param>
/// <param name="Playtime">The total played over the library; null while no store said.</param>
/// <param name="RecentPlaytime">What was played lately over the library, each store over its own
/// window (Steam: the last two weeks), from the readings younger than that window; null while none
/// said.</param>
/// <param name="RecentlyPlayed">The games with some recent playtime.</param>
/// <param name="TrackingSince">The date of the earliest reading that listed one of its games: its
/// history starts there. Null while no reading listed any.</param>
public sealed record LibraryStats(
    int Games,
    int Launched,
    int NeverLaunched,
    int Missing,
    TimeSpan? Playtime,
    TimeSpan? RecentPlaytime,
    int RecentlyPlayed,
    DateTimeOffset? TrackingSince);

/// <summary>What an owner played of a game on one day, as the readings show it: the growth of its
/// totals between two readings in a row, over every entry the game is owned through.</summary>
/// <param name="Date">The day, in UTC, of the reading that showed the growth: the time was played
/// between that reading and the one before, which with one reading a day is the day before as often
/// as that day.</param>
/// <param name="GameId">The canonical record.</param>
/// <param name="Playtime">More than zero.</param>
public sealed record PlayedDay(DateOnly Date, Guid GameId, TimeSpan Playtime);

/// <summary>A game's share of a day of a history.</summary>
public sealed record PlayedGame(Guid GameId, TimeSpan Playtime);

/// <summary>A day of a history: the games played, at least one, the most played first.</summary>
public sealed record HistoryDay(DateOnly Date, IReadOnlyList<PlayedGame> Games);

/// <summary>What an owner played since their library is read, day by day. The total a game had at its
/// first reading is not in it: nothing says when it was played.</summary>
/// <param name="TrackingSince">The same date as <see cref="LibraryStats.TrackingSince"/>: no day is
/// earlier.</param>
/// <param name="Days">The days something was played, the earliest first; the others are left out.</param>
public sealed record LibraryHistory(DateTimeOffset? TrackingSince, IReadOnlyList<HistoryDay> Days);

/// <summary>The sizes the catalogue accepts.</summary>
public static class Limits
{
    public const int Source = 32;

    public const int ExternalId = 128;

    public const int Name = 500;

    public const int DisplayName = 200;

    public const int SnapshotKind = 64;

    /// <summary>Where a store says a cover is: a file's name, with a folder at most.</summary>
    public const int CoverPath = 200;

    /// <summary>What is kept of a connector's words when a sync fails.</summary>
    public const int SyncDetail = 500;

    /// <summary>The most games one request for a library is answered with.</summary>
    public const int LibraryPage = 500;
}
