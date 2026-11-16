namespace Respawn.Games.Api;

// The bodies of the public API, version 1. Property names are written in snake_case on the wire.

public sealed record LibraryResponse(IReadOnlyList<LibraryGameResponse> Games);

/// <param name="GameId">The game, whatever the store.</param>
/// <param name="Name">The game's name; null while no source has named it.</param>
/// <param name="Source">The store it is owned through.</param>
/// <param name="ExternalId">The game's id in that store.</param>
/// <param name="PlaytimeSeconds">The total played, as the store counts it; null when it never said.</param>
/// <param name="LastPlayedAt">Null when the store does not say, or the game was never launched.</param>
public sealed record LibraryGameResponse(
    Guid GameId,
    string? Name,
    string Source,
    string ExternalId,
    long? PlaytimeSeconds,
    DateTimeOffset? LastPlayedAt)
{
    public static LibraryGameResponse From(OwnedGame game) =>
        new(game.GameId, game.Name, game.Source, game.ExternalId, game.Playtime is { } playtime ? (long)playtime.TotalSeconds : null, game.LastPlayedAt);
}
