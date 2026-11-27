namespace Respawn.Games.Ports;

/// <summary>What the game database (<see cref="IGameDataSource"/>) knows of the catalogue's games, asked
/// on a schedule of its own and kept once for the whole instance. Apart from the sync: whatever happens
/// here, the sync goes on.</summary>
public interface IGameDataService
{
    /// <summary>The stores whose games the database is to be asked about: those it is asked about with a
    /// store entry never asked, or asked longer ago than <see cref="GameDataSchedule.AskAgainAfter"/>
    /// (<see cref="GameDataSchedule.UnknownAskAgainAfter"/> for a game it did not know), and whose games
    /// it could be read about at their last turn or more than <see cref="GameDataSchedule.RetryAfter"/>
    /// ago in this process. None while the instance has no access to the database.</summary>
    Task<IReadOnlyList<string>> GetDueAsync(CancellationToken ct);

    /// <summary>Asks the database about a store's games that are due, at most
    /// <see cref="GameDataOptions.GamesPerTurn"/> of them, never asked first: which of its games each one
    /// is, then what it knows of those. Kept batch by batch, each in its own transaction: the answers as
    /// snapshots, the database's games, and the store entries' matches. A store entry the database knows
    /// as several of its games is taken as the one with the smallest id, and one it names but says
    /// nothing of as unknown. A database that cannot be read stops the run there; when the same games
    /// leave its answer unreadable <see cref="GameDataSchedule.UnreadableBeforeSetAside"/> turns in a row,
    /// they are set aside as unknown, for the games after them to be asked about. Any other failure
    /// leaves the store alone for <see cref="GameDataSchedule.RetryAfter"/> too, and is thrown.
    /// Null when there is nothing to do: no access, a store it is not asked about, a turn already on its
    /// way, one that failed less than <see cref="GameDataSchedule.RetryAfter"/> ago, or nothing due.</summary>
    Task<GameDataRead?> ReadAsync(string store, CancellationToken ct);
}
