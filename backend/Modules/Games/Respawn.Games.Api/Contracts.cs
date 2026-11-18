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
/// window its store counts (Steam: the last two weeks); null when no store says.</param>
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
/// <param name="RecentPlaytimeSeconds">What was played lately, as the store's latest answer said it; null when it did not say.</param>
/// <param name="LastPlayedAt">Null when the store does not say, or the game was never launched.</param>
/// <param name="FirstSeenAt">The date of the earliest answer of the store that listed it.</param>
/// <param name="AsOf">The date of the latest answer of the store that listed it: how fresh the rest is.</param>
public sealed record LibraryEntryResponse(
    Guid StoreEntryId,
    Guid LinkedAccountId,
    string Source,
    string ExternalId,
    long? PlaytimeSeconds,
    long? RecentPlaytimeSeconds,
    DateTimeOffset? LastPlayedAt,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset AsOf)
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
            entry.AsOf);
}

/// <param name="Accounts">The store accounts linked to the signed-in account, the earliest link first.</param>
public sealed record LinkedAccountsResponse(IReadOnlyList<LinkedAccountResponse> Accounts);

/// <param name="Id">The link. A library entry names it as its linked_account_id.</param>
/// <param name="Source">The store.</param>
/// <param name="ExternalId">The account's id in that store (Steam: the SteamID).</param>
/// <param name="DisplayName">The account's name in that store; null while the store was not asked.</param>
/// <param name="LinkedAt">When it was linked.</param>
public sealed record LinkedAccountResponse(Guid Id, string Source, string ExternalId, string? DisplayName, DateTimeOffset LinkedAt)
{
    public static LinkedAccountResponse From(LinkedAccount account) =>
        new(account.Id, account.Source, account.ExternalId, account.DisplayName, account.LinkedAt);
}

internal static class Seconds
{
    public static long? Of(TimeSpan? time) => time is { } said ? (long)said.TotalSeconds : null;
}
