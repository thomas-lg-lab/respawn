namespace Respawn.Games.Ports;

/// <summary>One game as an owner sees it. Nothing is written here, and no store is asked.</summary>
public interface IGameService
{
    /// <summary>A game of the catalogue, with what the owner owns of it: the game as their library
    /// gives it (<see cref="ILibraryService.GetLibraryAsync"/>), and its rank among the games they
    /// launched. A game the owner does not own is answered too, with no ownership: any game of the
    /// catalogue may be asked about. What was played of it over time is
    /// <see cref="IPlaytimeService.GetGameMonthsAsync"/>.</summary>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownGame"/>: the catalogue has no such
    /// game.</exception>
    Task<GameView> GetGameAsync(Guid ownerId, Guid gameId, CancellationToken ct);
}
