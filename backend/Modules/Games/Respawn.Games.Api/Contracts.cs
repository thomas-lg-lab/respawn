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
public sealed record LibraryGameResponse(
    Guid GameId,
    string? Name,
    long? PlaytimeSeconds,
    long? RecentPlaytimeSeconds,
    DateTimeOffset? LastPlayedAt,
    IReadOnlyList<LibraryEntryResponse> Entries)
{
    public static LibraryGameResponse From(OwnedGame game) =>
        new(
            game.GameId,
            game.Name,
            Seconds.Of(game.Playtime),
            Seconds.Of(game.RecentPlaytime),
            game.LastPlayedAt,
            [.. game.Entries.Select(LibraryEntryResponse.From)]);
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
