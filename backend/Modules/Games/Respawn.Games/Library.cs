using System.Globalization;

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
/// No reading sets it and none clears it: a game leaves a library when its owner says so, and comes
/// back when they say so. What was known of it and its history stay, and the readings go on saying
/// what the store says of it.</param>
/// <param name="MissingSince">The date of the earliest complete reading that left it out, while none
/// has listed it since (a refund, a share that ended, a free weekend over, or an answer cut short that
/// still agrees with itself: nothing here can tell which). A game of the library stays in it, flagged;
/// null once a reading lists it again. Kept up to date for a game out of the library too.</param>
/// <param name="ListedAgainSince">For a game out of the library, the date of the earliest reading that
/// listed it again, taken after it left, while none has left it out since: the store has it back, and
/// its owner is told. Null in the library.</param>
/// <param name="NoticeDismissedAt">When its owner said they do not want to be told the game is back,
/// for good: whatever a store says afterwards, of this entry or of another taken out with it. Null in
/// the library.</param>
public sealed record Ownership(
    Guid StoreEntryId,
    TimeSpan? Playtime,
    DateTimeOffset? LastPlayedAt,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset AsOf,
    Guid SnapshotId,
    TimeSpan? RecentPlaytime = null,
    DateTimeOffset? UnlistedSince = null,
    DateTimeOffset? MissingSince = null,
    DateTimeOffset? ListedAgainSince = null,
    DateTimeOffset? NoticeDismissedAt = null);

/// <summary>What is known of a store entry through one linked account.</summary>
public sealed record AccountOwnership(Guid LinkedAccountId, Ownership Ownership);

/// <summary>One way an owner owned a game they took out of their library.</summary>
/// <param name="RemovedAt">When its owner took it out.</param>
/// <param name="ListedAgainSince">The date of the earliest reading that listed it again, while none has
/// left it out since; null while its store does not list it.</param>
/// <param name="NoticeDismissedAt">When its owner dismissed, for good, the notice that it is back.</param>
public sealed record RemovedEntry(
    Guid GameId,
    string? GameName,
    Guid StoreEntryId,
    Guid LinkedAccountId,
    string Source,
    string ExternalId,
    DateTimeOffset RemovedAt,
    DateTimeOffset? ListedAgainSince = null,
    DateTimeOffset? NoticeDismissedAt = null);

/// <summary>A game an owner took out of their library, once, however many entries were taken out.</summary>
/// <param name="GameId">The canonical record.</param>
/// <param name="Name">Null while no source has named the game.</param>
/// <param name="BackSince">The notice its owner is owed: the earliest date a store listed again one of
/// its entries, among those whose notice was not dismissed. Null when there is nothing to tell.</param>
/// <param name="Entries">The entries taken out, at least one.</param>
public sealed record RemovedGame(Guid GameId, string? Name, DateTimeOffset? BackSince, IReadOnlyList<RemovedEntry> Entries);

/// <summary>One way an owner owns a game: a store entry, through one of their linked accounts. The pair
/// of the two ids is unique in a library. With it, the source of the cover the owner chose for the
/// game, when they chose one.</summary>
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
    DateTimeOffset? MissingSince = null,
    string? CoverSource = null);

/// <summary>A game of an owner's library, once, however many store entries it is owned through: after
/// a merge, or with two accounts of one store, a game has several.</summary>
/// <param name="GameId">The canonical record.</param>
/// <param name="Name">Null while no source has named the game.</param>
/// <param name="Playtime">The sum over its entries; null while none said.</param>
/// <param name="RecentPlaytime">The sum over its entries whose reading is younger than its store's
/// window; null while none said.</param>
/// <param name="LastPlayedAt">The latest over its entries.</param>
/// <param name="Entries">The ways it is owned, at least one.</param>
/// <param name="CoverSource">The source of the cover the owner chose for it; null while they chose none.</param>
/// <param name="Genres">Its genres, as the game database says them (<see cref="GameDataSaid.Genres"/>);
/// null where they were not read, empty when the database gave none.</param>
/// <param name="Themes">Its themes, likewise.</param>
/// <param name="NormallySeconds">How long players say it takes at a normal pace, as the game database
/// says it; null where it was not read or none was said.</param>
/// <param name="StoreTags">The tags its stores' players gave it, as keys (<see cref="StoreTagKeys"/>), the
/// heaviest first; null where they were not read, empty when no store gave any.</param>
public sealed record OwnedGame(
    Guid GameId,
    string? Name,
    TimeSpan? Playtime,
    TimeSpan? RecentPlaytime,
    DateTimeOffset? LastPlayedAt,
    IReadOnlyList<OwnedEntry> Entries,
    string? CoverSource = null,
    IReadOnlyList<string>? Genres = null,
    IReadOnlyList<string>? Themes = null,
    int? NormallySeconds = null,
    IReadOnlyList<string>? StoreTags = null);

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
/// <param name="Date">The day, in UTC, the time was played on, as far as the store says: the day the
/// game was last played, when the store gives that moment and it lies between the reading that showed
/// the growth and the one before; otherwise the day of that reading. A reading of the small hours
/// holds the evening before it, and this is what keeps that evening in its own day, month and year.</param>
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

    /// <summary>A kept image's kind, and the key a source names it by (<see cref="ImageName"/>).</summary>
    public const int ImageKind = 16;

    public const int ImageKey = 100;

    /// <summary>An image's media type.</summary>
    public const int ContentType = 32;

    /// <summary>What is kept of a connector's words when a sync fails.</summary>
    public const int SyncDetail = 500;

    /// <summary>The most games one request for a library is answered with.</summary>
    public const int LibraryPage = 500;

    /// <summary>A short word a game database gives: a slug, an image's id, a genre or a theme.</summary>
    public const int GameDataWord = 200;

    /// <summary>The address of a game's page in a game database.</summary>
    public const int GameDataUrl = 500;

    /// <summary>What a game database says a game is about.</summary>
    public const int GameDataSummary = 20_000;

    /// <summary>A tag's id in its store.</summary>
    public const int StoreTagId = 32;

    /// <summary>A tag's name in one language, as its store gives it.</summary>
    public const int StoreTagName = 100;

    /// <summary>A language, as the app names it (<see cref="StoreTagSchedule.Languages"/>).</summary>
    public const int Language = 8;
}

/// <summary>The ids stores give their games, as a connector checks one before it goes in an address or
/// in a question.</summary>
public static class StoreIds
{
    /// <summary>Whether this is a Steam app id as Steam writes it: a number of 32 bits in digits alone,
    /// with no zero before it.</summary>
    public static bool IsSteamAppId(string externalId) =>
        uint.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
        && number.ToString(CultureInfo.InvariantCulture) == externalId;
}
