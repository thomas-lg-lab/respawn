namespace Respawn.Games.Ports;

/// <summary>One game as an owner sees it, and the cover they choose for it. No store is asked.</summary>
public interface IGameService
{
    /// <summary>Chooses the cover the owner sees for a game, by its source, among the game's
    /// (<see cref="GameCovers.Sources"/>); null, or the game's own cover's source, goes back to the
    /// game's own. Theirs alone: every other account keeps seeing what it chose.</summary>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownGame"/>; or
    /// <see cref="GamesError.NoSuchCover"/>: the game has no cover from that source.</exception>
    Task<GameCovers> ChooseCoverAsync(Guid ownerId, Guid gameId, string? source, CancellationToken ct);

    /// <summary>A game of the catalogue, with what the owner owns of it: the game as their library
    /// gives it (<see cref="ILibraryService.GetLibraryAsync"/>), its rank among the games they
    /// launched, and its covers with the one they chose. A game the owner does not own is answered too, with no ownership: any game of the
    /// catalogue may be asked about. What was played of it over time is
    /// <see cref="IPlaytimeService.GetGameMonthsAsync"/>.</summary>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownGame"/>: the catalogue has no such
    /// game.</exception>
    Task<GameView> GetGameAsync(Guid ownerId, Guid gameId, CancellationToken ct);

    /// <summary>The sources whose data the instance shows and credits: the game database, once the
    /// instance has access to it or holds anything it said. Empty otherwise.</summary>
    Task<IReadOnlyList<Credit>> GetCreditsAsync(CancellationToken ct);
}
