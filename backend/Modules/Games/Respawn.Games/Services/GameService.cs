using Respawn.Games.Ports;

namespace Respawn.Games.Services;

/// <summary>One game as an owner sees it: the catalogue's record and what their library says of it.</summary>
public sealed class GameService(
    ICatalogue catalogue,
    ILibraryStore library,
    IEnumerable<ILibrarySource> sources,
    IGameDataSource gameData,
    TimeProvider clock) : IGameService
{
    public async Task<IReadOnlyList<Credit>> GetCreditsAsync(CancellationToken ct) =>
        gameData.HasAccess || await catalogue.HasGameDataAsync(ct)
            ? [new Credit(gameData.Source, gameData.Label, gameData.Home)]
            : [];

    public async Task<GameView> GetGameAsync(Guid ownerId, Guid gameId, CancellationToken ct)
    {
        var game = await catalogue.FindGameAsync(gameId, ct)
            ?? throw new GamesException(GamesError.UnknownGame);

        // The whole library, as a rank is a place in it: one reading, a row per game owned.
        var games = OwnedGames.Of(await library.ListAsync(ownerId, ct), sources, clock.GetUtcNow());
        var owned = games.FirstOrDefault(other => other.GameId == gameId);
        return new GameView(
            game with
            {
                // An order that does not change from one answer to the next.
                Stores =
                [
                    .. game.Stores
                        .OrderBy(store => store.Source, StringComparer.Ordinal)
                        .ThenBy(store => store.ExternalId, StringComparer.Ordinal),
                ],
            },
            owned,
            Rank(owned, games),
            RemovedGames.Of(await library.ListRemovedAsync(ownerId, gameId, ct)).SingleOrDefault(),
            await CoversAsync(ownerId, gameId, ct),
            game.GameDataUrl is { } url ? new GameDataPage(gameData.Source, url) : null);
    }

    public async Task<GameCovers> ChooseCoverAsync(Guid ownerId, Guid gameId, string? source, CancellationToken ct)
    {
        _ = await catalogue.FindGameAsync(gameId, ct) ?? throw new GamesException(GamesError.UnknownGame);
        var sources = await CoverSourcesAsync(gameId, ct);
        if (source is not null && !sources.Contains(source, StringComparer.Ordinal))
        {
            throw new GamesException(GamesError.NoSuchCover);
        }

        // The game's own cover is no choice: it is what the game shows to whoever chose none, and it
        // follows the sources when they change.
        var chosen = source == sources.FirstOrDefault() ? null : source;
        await library.SetCoverChoiceAsync(ownerId, gameId, chosen, clock.GetUtcNow(), ct);
        return new GameCovers(sources, chosen);
    }

    /// <summary>The covers of a game, and the owner's choice while it is still one of them.</summary>
    private async Task<GameCovers> CoversAsync(Guid ownerId, Guid gameId, CancellationToken ct)
    {
        var sources = await CoverSourcesAsync(gameId, ct);
        return new GameCovers(sources, CoverOrigins.Chosen(sources, await library.FindCoverChoiceAsync(ownerId, gameId, ct)));
    }

    /// <summary>The sources a game has a cover from, in the order its own cover is looked for: the
    /// stores that said where theirs is, then the game database when it gives one.</summary>
    private async Task<IReadOnlyList<string>> CoverSourcesAsync(Guid gameId, CancellationToken ct) =>
        ((await catalogue.FindCoverOriginsAsync([gameId], ct)).GetValueOrDefault(gameId) ?? CoverOrigins.None)
            .Sources(gameData.Source);

    /// <summary>The game's place among those launched, the most played first. Two games played as long
    /// share a position, and the next one is counted after both.</summary>
    private static GameRank? Rank(OwnedGame? game, List<OwnedGame> games)
    {
        if (game is null || !OwnedGames.Launched(game))
        {
            return null;
        }

        var launched = games.Where(OwnedGames.Launched).ToList();
        var playtime = game.Playtime ?? TimeSpan.Zero;
        return new GameRank(1 + launched.Count(other => (other.Playtime ?? TimeSpan.Zero) > playtime), launched.Count);
    }
}
