using System.ComponentModel.DataAnnotations;

namespace Respawn.Games.Api;

// The bodies of the public API, version 1. Property names are written in snake_case on the wire.

/// <param name="Total">How many games the library has, whatever part of it this answer holds.</param>
/// <param name="Offset">How many games come before the first of this answer, in the order asked.</param>
/// <param name="Limit">The most games this answer could hold.</param>
/// <param name="Games">The games, one item each.</param>
public sealed record LibraryResponse(int Total, int Offset, int Limit, IReadOnlyList<LibraryGameResponse> Games);

/// <param name="GameId">The game, whatever the store.</param>
/// <param name="Name">The game's name; null while no source has named it.</param>
/// <param name="PlaytimeSeconds">The total played, added up over its entries; null when no store ever said.</param>
/// <param name="RecentPlaytimeSeconds">What was played lately, added up over its entries, each over the
/// window its store counts (Steam: the last two weeks); null when no store says, or what each said is
/// older than its window.</param>
/// <param name="LastPlayedAt">Null when no store says, or the game was never launched.</param>
/// <param name="Entries">The ways the game is owned: one per store entry and linked account, at least one.</param>
/// <param name="CoverSource">The source of the cover the account chose for the game, to ask for in the
/// cover's source parameter; null while it chose none, and the game's own cover is shown.</param>
/// <param name="Genres">The game's genres, as IGDB names them (English); empty while IGDB says none.</param>
/// <param name="Themes">The game's themes, likewise.</param>
/// <param name="NormallySeconds">How long players say it takes at a normal pace, as IGDB says it; null
/// while it says none.</param>
/// <param name="StoreTags">The tags its stores' players gave it, as keys (<c>steam:1628</c>), the heaviest
/// first, each once; empty while no store gave any. Their names are those of <c>GET /api/v1/store-tags</c>.</param>
public sealed record LibraryGameResponse(
    Guid GameId,
    string? Name,
    long? PlaytimeSeconds,
    long? RecentPlaytimeSeconds,
    DateTimeOffset? LastPlayedAt,
    IReadOnlyList<LibraryEntryResponse> Entries,
    string? CoverSource,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Themes,
    int? NormallySeconds,
    IReadOnlyList<string> StoreTags)
{
    public static LibraryGameResponse From(OwnedGame game) =>
        new(
            game.GameId,
            game.Name,
            Seconds.Of(game.Playtime),
            Seconds.Of(game.RecentPlaytime),
            game.LastPlayedAt,
            [.. game.Entries.Select(LibraryEntryResponse.From)],
            game.CoverSource,
            game.Genres ?? [],
            game.Themes ?? [],
            game.NormallySeconds,
            game.StoreTags ?? []);
}

/// <param name="Tags">Every tag a store gave a game of the instance and named, in no order.</param>
public sealed record StoreTagsResponse(IReadOnlyList<StoreTagResponse> Tags)
{
    public static StoreTagsResponse From(IReadOnlyList<NamedStoreTag> tags) => new([.. tags.Select(StoreTagResponse.From)]);
}

/// <param name="Key">The tag, as the games carry it (<c>steam:1628</c>).</param>
/// <param name="Names">Its name as its store gives it, by language (<c>en</c>, <c>fr</c>); a language the
/// store gave no name in is left out.</param>
public sealed record StoreTagResponse(string Key, IReadOnlyDictionary<string, string> Names)
{
    public static StoreTagResponse From(NamedStoreTag tag) => new(tag.Key, tag.Names);
}

/// <param name="StoreEntryId">The game as its store knows it. With <paramref name="LinkedAccountId"/>, unique in a library.</param>
/// <param name="LinkedAccountId">The store account it is owned through.</param>
/// <param name="Source">The store.</param>
/// <param name="ExternalId">The game's id in that store.</param>
/// <param name="PlaytimeSeconds">The total played, as the store counts it; null when it never said.</param>
/// <param name="RecentPlaytimeSeconds">What was played lately, as the store's latest answer said it; null when it did not
/// say, or that answer is older than the window the store counts over (Steam: two weeks).</param>
/// <param name="LastPlayedAt">Null when the store does not say, or the game was never launched.</param>
/// <param name="FirstSeenAt">The date of the earliest answer of the store that listed it.</param>
/// <param name="AsOf">The date of the latest answer of the store that listed it: how fresh the rest is.</param>
/// <param name="MissingSince">The date of the earliest complete answer of the store that left it out, when
/// none has listed it since: a refund, a share that ended, a free weekend over, or an answer cut short.
/// The game stays in the library; null while the store lists it.</param>
public sealed record LibraryEntryResponse(
    Guid StoreEntryId,
    Guid LinkedAccountId,
    string Source,
    string ExternalId,
    long? PlaytimeSeconds,
    long? RecentPlaytimeSeconds,
    DateTimeOffset? LastPlayedAt,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset AsOf,
    DateTimeOffset? MissingSince)
{
    public static LibraryEntryResponse From(OwnedEntry entry) =>
        new(
            entry.StoreEntryId,
            entry.LinkedAccountId,
            entry.Source,
            entry.ExternalId,
            Seconds.Of(entry.Playtime),
            Seconds.Of(entry.RecentPlaytime),
            entry.LastPlayedAt,
            entry.FirstSeenAt,
            entry.AsOf,
            entry.MissingSince);
}

/// <param name="Accounts">The store accounts linked to the signed-in account, the earliest link first.</param>
public sealed record LinkedAccountsResponse(IReadOnlyList<LinkedAccountResponse> Accounts);

/// <param name="Id">The link. A library entry names it as its linked_account_id.</param>
/// <param name="Source">The store.</param>
/// <param name="ExternalId">The account's id in that store (Steam: the SteamID).</param>
/// <param name="DisplayName">The account's name in that store; null while the store was not asked.</param>
/// <param name="LinkedAt">When it was linked.</param>
/// <param name="HasOwnKey">Whether its owner gave their own key of the store on it, which every sync then
/// reads it with. The key itself is never answered.</param>
/// <param name="Sync">Where the reading of its library stands.</param>
public sealed record LinkedAccountResponse(
    Guid Id, string Source, string ExternalId, string? DisplayName, DateTimeOffset LinkedAt, bool HasOwnKey, SyncStateResponse Sync)
{
    public static LinkedAccountResponse From(LinkedAccount account, SyncState sync) =>
        new(
            account.Id,
            account.Source,
            account.ExternalId,
            account.DisplayName,
            account.LinkedAt,
            account.HasCredential,
            SyncStateResponse.From(sync));
}

/// <param name="Key">The owner's own key of the store (Steam: a Steam Web API key, letters and digits).</param>
public sealed class OwnKeyRequest
{
    [Required, StringLength(256)]
    public string Key { get; init; } = "";
}

/// <param name="Running">It is being read right now, every day's reading or one asked for.</param>
/// <param name="Last">The last sync that ended, whatever its outcome; null while none did. A failed one
/// says why in its error. A sync on its way is not one yet: while running, this is the one before.</param>
/// <param name="LastSucceededAt">When the last sync that recorded a library ended: how fresh the
/// library is. A sync whose outcome is nothing_visible is not one. Null while none did.</param>
public sealed record SyncStateResponse(bool Running, SyncRunResponse? Last, DateTimeOffset? LastSucceededAt)
{
    public static SyncStateResponse From(SyncState state) =>
        new(state.Running, state.Last is { } last ? SyncRunResponse.From(last) : null, state.LastSucceededAt);
}

/// <summary>Counted over the same games as the library, those flagged as missing included. What needs a
/// history (a year, a streak) is not here.</summary>
/// <param name="Games">The games, each once however many entries it is owned through.</param>
/// <param name="Launched">The games a store says were played: some playtime, or a last session.</param>
/// <param name="NeverLaunched">The others: no store says they were ever played.</param>
/// <param name="Missing">The games every entry of which is flagged as missing.</param>
/// <param name="PlaytimeSeconds">The total played; null while no store said.</param>
/// <param name="RecentPlaytimeSeconds">What was played lately, each store over its own window (Steam:
/// the last two weeks), from the readings younger than that window; null while none said.</param>
/// <param name="RecentlyPlayed">The games with some recent playtime.</param>
/// <param name="TrackingSince">The date of the earliest reading that listed one of the games: the
/// library's history starts there. Null while no reading listed any.</param>
public sealed record LibraryStatsResponse(
    int Games,
    int Launched,
    int NeverLaunched,
    int Missing,
    long? PlaytimeSeconds,
    long? RecentPlaytimeSeconds,
    int RecentlyPlayed,
    DateTimeOffset? TrackingSince)
{
    public static LibraryStatsResponse From(LibraryStats stats) =>
        new(
            stats.Games,
            stats.Launched,
            stats.NeverLaunched,
            stats.Missing,
            Seconds.Of(stats.Playtime),
            Seconds.Of(stats.RecentPlaytime),
            stats.RecentlyPlayed,
            stats.TrackingSince);
}

/// <summary>What was played day by day since the library is read. A store gives totals and no history:
/// a day's time is the growth of those totals between two readings in a row, so the total a game had
/// at its first reading is not here, and with one reading a day the time may have been played the day
/// before the date it is counted on.</summary>
/// <param name="TrackingSince">The same date as in the library's figures: the history starts there.
/// Null while no reading listed a game.</param>
/// <param name="Days">The days something was played, the earliest first. A day with nothing is left out.</param>
public sealed record LibraryHistoryResponse(DateTimeOffset? TrackingSince, IReadOnlyList<HistoryDayResponse> Days)
{
    public static LibraryHistoryResponse From(LibraryHistory history) =>
        new(history.TrackingSince, [.. history.Days.Select(HistoryDayResponse.From)]);
}

/// <param name="Date">The day, in UTC, of the reading that showed the time played.</param>
/// <param name="Games">The games played that day, at least one, the most played first.</param>
public sealed record HistoryDayResponse(DateOnly Date, IReadOnlyList<HistoryGameResponse> Games)
{
    public static HistoryDayResponse From(HistoryDay day) =>
        new(day.Date, [.. day.Games.Select(game => new HistoryGameResponse(game.GameId, (long)game.Playtime.TotalSeconds))]);
}

/// <param name="GameId">The game, as the library names it.</param>
/// <param name="Seconds">What was played of it that day, added up over its entries; more than zero.</param>
public sealed record HistoryGameResponse(Guid GameId, long Seconds);

/// <summary>What was played year by year, the earliest first.</summary>
/// <param name="Years">The years something could be counted for: those a review covers, then each year
/// from the first reading to today. Empty while nothing was read.</param>
public sealed record LibraryYearsResponse(IReadOnlyList<YearResponse> Years)
{
    public static LibraryYearsResponse From(IReadOnlyList<PlayedYear> years) =>
        new([.. years.Select(YearResponse.From)]);
}

/// <param name="Year">The calendar year, in UTC.</param>
/// <param name="Seconds">What was played from first_day to last_day.</param>
/// <param name="Source">review: the store's review of the year, every month of it, every game played,
/// owned or not. readings: the growth of the library's totals between two daily readings, from the first
/// reading on; what a game's total was at its first reading is not in it.</param>
/// <param name="FirstDay">The first day counted.</param>
/// <param name="LastDay">The last day counted: the year's last, or today.</param>
/// <param name="Whole">True when first_day and last_day are the year's first and last days.</param>
public sealed record YearResponse(int Year, long Seconds, string Source, DateOnly FirstDay, DateOnly LastDay, bool Whole)
{
    public static YearResponse From(PlayedYear year) =>
        new(
            year.Year,
            (long)year.Playtime.TotalSeconds,
            YearWords.Of(year.Source),
            year.From,
            year.To,
            year.Whole);
}

/// <summary>One game, as the signed-in account sees it: the catalogue's record and what the account
/// owns of it.</summary>
/// <param name="GameId">The game, whatever the store.</param>
/// <param name="Name">The game's name; null while no source has named it.</param>
/// <param name="Stores">The game as each store knows it, whoever owns it.</param>
/// <param name="PlaytimeSeconds">The total played, added up over its entries; null when no store ever said,
/// or the game is not in the library.</param>
/// <param name="RecentPlaytimeSeconds">What was played lately, as in the library; null when no store says.</param>
/// <param name="LastPlayedAt">Null when no store says, or the game was never launched.</param>
/// <param name="Entries">The ways the account owns the game, as in the library: one per store entry and
/// linked account, each with its own playtime and missing_since. Empty when the game is not in the library.</param>
/// <param name="Rank">Where the game stands among the games launched; null when it was never launched.</param>
/// <param name="Removed">What the account took out of its library of this game, as the removed games
/// give it; null when nothing was.</param>
/// <param name="GameData">What the game database (IGDB) says of the game, when it knows it; null
/// otherwise, or while it was not asked.</param>
/// <param name="Covers">The sources the game has a cover from, and the one the account chose.</param>
public sealed record GameResponse(
    Guid GameId,
    string? Name,
    IReadOnlyList<GameStoreResponse> Stores,
    long? PlaytimeSeconds,
    long? RecentPlaytimeSeconds,
    DateTimeOffset? LastPlayedAt,
    IReadOnlyList<LibraryEntryResponse> Entries,
    GameRankResponse? Rank,
    RemovedGameResponse? Removed,
    GameDataResponse? GameData,
    GameCoversResponse Covers)
{
    public static GameResponse From(GameView view) =>
        new(
            view.Game.Id,
            view.Game.Name,
            [.. view.Game.Stores.Select(store => new GameStoreResponse(store.StoreEntryId, store.Source, store.ExternalId))],
            Seconds.Of(view.Owned?.Playtime),
            Seconds.Of(view.Owned?.RecentPlaytime),
            view.Owned?.LastPlayedAt,
            [.. (view.Owned?.Entries ?? []).Select(LibraryEntryResponse.From)],
            view.Rank is { } rank ? new GameRankResponse(rank.Position, rank.Launched) : null,
            view.Removed is { } removed ? RemovedGameResponse.From(removed) : null,
            view.GameData is { } said ? GameDataResponse.From(said) : null,
            GameCoversResponse.From(view.Covers));
}

/// <summary>What the game database says of a game, in its own words (IGDB: English).</summary>
/// <param name="Source">The game database: igdb.</param>
/// <param name="Url">The game's page there, for a person to follow; null when the database gave none.</param>
/// <param name="Genres">Its genres, in the database's order; empty when it gave none.</param>
/// <param name="Themes">Its themes, likewise.</param>
/// <param name="Summary">What the game is about; null when the database says nothing.</param>
/// <param name="TimeToBeat">How long players say it takes; null when none said.</param>
/// <param name="ReleasedOn">The day the game first came out, anywhere (IGDB dates it in UTC); null when
/// the database does not say.</param>
/// <param name="Similar">The games of the account's library the database lists as close to this one, in
/// its order, four at most; empty when it lists none the account has.</param>
public sealed record GameDataResponse(
    string Source,
    string? Url,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Themes,
    string? Summary,
    TimeToBeatResponse? TimeToBeat,
    DateOnly? ReleasedOn,
    IReadOnlyList<SimilarGameResponse> Similar)
{
    public static GameDataResponse From(GameDataSaid said) =>
        new(
            said.Source,
            said.Url,
            said.Genres,
            said.Themes,
            said.Summary,
            said.TimeToBeat is { } time
                ? new TimeToBeatResponse(time.Hastily, time.Normally, time.Completely, time.Count)
                : null,
            said.ReleasedOn,
            [.. said.Similar.Select(SimilarGameResponse.From)]);
}

/// <summary>A game of the account's library close to the one shown, as its row there says it.</summary>
/// <param name="GameId">The game, as the library has it.</param>
/// <param name="Name">Null when no source has named it.</param>
/// <param name="PlaytimeSeconds">As in the library: null when no store says.</param>
/// <param name="LastPlayedAt">As in the library: null when no store says, or it was never launched.</param>
/// <param name="CoverSource">As in the library: the cover the account chose, null for the game's own.</param>
/// <param name="NormallySeconds">How long players say it takes at a normal pace; null when none said.</param>
public sealed record SimilarGameResponse(
    Guid GameId,
    string? Name,
    long? PlaytimeSeconds,
    DateTimeOffset? LastPlayedAt,
    string? CoverSource,
    int? NormallySeconds)
{
    public static SimilarGameResponse From(SimilarGame similar) =>
        new(
            similar.Game.GameId,
            similar.Game.Name,
            Seconds.Of(similar.Game.Playtime),
            similar.Game.LastPlayedAt,
            similar.Game.CoverSource,
            similar.NormallySeconds);
}

/// <summary>How long the players who said so took to finish a game, in seconds. Each is null when
/// nobody said.</summary>
/// <param name="HastilySeconds">Going straight to the end.</param>
/// <param name="NormallySeconds">At a usual pace, the main story and some of the rest.</param>
/// <param name="CompletelySeconds">Doing everything the game holds.</param>
/// <param name="Count">How many players the figures rest on; null when the database did not say.</param>
public sealed record TimeToBeatResponse(int? HastilySeconds, int? NormallySeconds, int? CompletelySeconds, int? Count);

/// <summary>A game's covers, as the signed-in account sees them.</summary>
/// <param name="Sources">The sources the game has a cover from (steam, igdb): the stores that said where
/// theirs is, then IGDB when it has one. The first is the game's own cover, shown to whoever chose
/// none: once the instance keeps it, the source of the one it shows. Empty when none has.</param>
/// <param name="Chosen">The source the account chose, one of the others; null while it chose none,
/// or what it chose is gone.</param>
public sealed record GameCoversResponse(IReadOnlyList<string> Sources, string? Chosen)
{
    public static GameCoversResponse From(GameCovers covers) => new(covers.Sources, covers.Chosen);
}

/// <param name="Source">The source of the cover to show: one of the game's covers' sources (steam, igdb).</param>
public sealed class CoverChoiceRequest
{
    [Required, StringLength(Limits.Source)]
    public string Source { get; init; } = "";
}

/// <summary>The sources the instance credits.</summary>
/// <param name="Credits">One per source; empty when none is to be credited.</param>
public sealed record CreditsResponse(IReadOnlyList<CreditResponse> Credits)
{
    public static CreditsResponse From(IReadOnlyList<Credit> credits) =>
        new([.. credits.Select(credit => new CreditResponse(credit.Source, credit.Label, credit.Url.ToString()))]);
}

/// <param name="Source">The source, as the API names it: igdb.</param>
/// <param name="Label">Its name, as people know it.</param>
/// <param name="Url">Its home page, which the credit leads to.</param>
public sealed record CreditResponse(string Source, string Label, string Url);

/// <summary>The games the signed-in account took out of its library.</summary>
/// <param name="Games">One item per game, by name.</param>
public sealed record RemovedGamesResponse(IReadOnlyList<RemovedGameResponse> Games);

/// <param name="GameId">The game, whatever the store.</param>
/// <param name="Name">The game's name; null while no source has named it.</param>
/// <param name="BackSince">The notice to show: the date of the earliest answer of a store that listed
/// the game again, among the entries whose notice was not dismissed. Null when no store lists it again,
/// or the notice was dismissed.</param>
/// <param name="Entries">The entries taken out, at least one.</param>
public sealed record RemovedGameResponse(Guid GameId, string? Name, DateTimeOffset? BackSince, IReadOnlyList<RemovedEntryResponse> Entries)
{
    public static RemovedGameResponse From(RemovedGame game) =>
        new(
            game.GameId,
            game.Name,
            game.BackSince,
            [
                .. game.Entries.Select(entry => new RemovedEntryResponse(
                    entry.StoreEntryId,
                    entry.LinkedAccountId,
                    entry.Source,
                    entry.ExternalId,
                    entry.RemovedAt,
                    entry.ListedAgainSince,
                    entry.NoticeDismissedAt)),
            ]);
}

/// <param name="StoreEntryId">The game as its store knows it.</param>
/// <param name="LinkedAccountId">The store account it was owned through.</param>
/// <param name="Source">The store.</param>
/// <param name="ExternalId">The game's id in that store.</param>
/// <param name="RemovedAt">When it was taken out of the library.</param>
/// <param name="ListedAgainSince">The date of the earliest answer of the store that listed it again,
/// while none has left it out since; null while the store does not list it.</param>
/// <param name="NoticeDismissedAt">When the notice that it is back was dismissed, for good; null otherwise.</param>
public sealed record RemovedEntryResponse(
    Guid StoreEntryId,
    Guid LinkedAccountId,
    string Source,
    string ExternalId,
    DateTimeOffset RemovedAt,
    DateTimeOffset? ListedAgainSince,
    DateTimeOffset? NoticeDismissedAt);

/// <param name="StoreEntryId">The game as that store knows it. A library entry names it as its store_entry_id.</param>
/// <param name="Source">The store.</param>
/// <param name="ExternalId">The game's id in that store (Steam: the app id).</param>
public sealed record GameStoreResponse(Guid StoreEntryId, string Source, string ExternalId);

/// <param name="Position">One for the most played game of the library; two games played as long share theirs.</param>
/// <param name="Launched">How many games of the library were launched, this one included: the same
/// figure as launched in the library's figures.</param>
public sealed record GameRankResponse(int Position, int Launched);

/// <summary>What was played of one game, month by month.</summary>
/// <param name="Months">The months the game was played in, the earliest first. A month with nothing
/// played is left out: the years of the library say which days are counted at all.</param>
public sealed record GameMonthsResponse(IReadOnlyList<GameMonthResponse> Months)
{
    public static GameMonthsResponse From(IReadOnlyList<GameMonth> months) =>
        new([.. months.Select(month => new GameMonthResponse(month.Month, (long)month.Playtime.TotalSeconds, YearWords.Of(month.Source)))]);
}

/// <param name="Month">The first day of the month.</param>
/// <param name="Seconds">What was played of the game that month, added up over its entries; more than zero.</param>
/// <param name="Source">review: the store's review of that year, exact to the month. readings: the growth
/// of the game's totals between the daily readings of that month, in UTC.</param>
public sealed record GameMonthResponse(DateOnly Month, long Seconds, string Source);

/// <summary>What was played in one month, game by game.</summary>
/// <param name="Month">The first day of the month.</param>
/// <param name="Source">review or readings, as the month's year is counted; null when nothing counts that
/// month: its year has no review, and the readings had not started, or the month is still to come.</param>
/// <param name="FirstDay">The first day counted; null with no source.</param>
/// <param name="LastDay">The last day counted; null with no source.</param>
/// <param name="Games">The games played that month, the most played first; empty when nothing was.</param>
public sealed record LibraryMonthResponse(
    DateOnly Month, string? Source, DateOnly? FirstDay, DateOnly? LastDay, IReadOnlyList<MonthGameResponse> Games)
{
    public static LibraryMonthResponse From(LibraryMonth month) =>
        new(
            month.Month,
            month.Source is { } source ? YearWords.Of(source) : null,
            month.From,
            month.To,
            [.. month.Games.Select(game => new MonthGameResponse(game.GameId, game.Name, (long)game.Playtime.TotalSeconds))]);
}

/// <param name="GameId">The game. A review counts every game played: it may not be in the library.</param>
/// <param name="Name">The game's name; null while no source has named it.</param>
/// <param name="Seconds">What was played of it that month; more than zero.</param>
public sealed record MonthGameResponse(Guid GameId, string? Name, long Seconds);

/// <summary>Where a time was counted from, as the API spells it.</summary>
internal static class YearWords
{
    public static string Of(YearSource source) => source switch
    {
        YearSource.Review => "review",
        YearSource.Readings => "readings",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}

/// <param name="Id">The sync.</param>
/// <param name="LinkedAccountId">The store account that was read.</param>
/// <param name="Trigger">Why it ran: scheduled (the reading of the day) or manual (it was asked for).</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="FinishedAt">When it ended.</param>
/// <param name="Outcome">succeeded: the store answered a library, and it was recorded. nothing_visible:
/// the store answered and showed no game, as it does for a profile whose game details are private; the
/// library is left as it was. failed: the store could not be read.</param>
/// <param name="Error">Why it failed, null unless it did: not_configured (the instance has no access to
/// the store), refused (the store refused that access), unavailable (the store did not answer; trying
/// again may work) or unreadable (the store answered something that cannot be read).</param>
/// <param name="OwnKey">True when it read, or was to read, with the key its owner gave on the account
/// rather than the instance's: a refused, or a not_configured (the instance can no longer read the key it
/// kept), is then that key's. A key given is the only one tried.</param>
public sealed record SyncRunResponse(
    Guid Id,
    Guid LinkedAccountId,
    string Trigger,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string Outcome,
    string? Error,
    bool OwnKey)
{
    public static SyncRunResponse From(SyncRun run) =>
        new(
            run.Id,
            run.LinkedAccountId,
            SyncWords.Of(run.Trigger),
            run.StartedAt,
            run.FinishedAt,
            SyncWords.Of(run.Outcome),
            run.Error is { } error ? SyncWords.Of(error) : null,
            run.OwnCredential);
}

/// <summary>A sync, as the API spells it.</summary>
internal static class SyncWords
{
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
}

internal static class Seconds
{
    public static long? Of(TimeSpan? time) => time is { } said ? (long)said.TotalSeconds : null;
}
