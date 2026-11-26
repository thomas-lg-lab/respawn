namespace Respawn.Games.Ports;

/// <summary>What an owner played over time, read from what the sync and the import of the reviews
/// kept. One rule says where a year is counted from, and every reading here follows it: a year every
/// linked account has a review of is the reviews'; the years from the first reading on that are not
/// are the daily readings' (<see cref="ILibraryService.GetHistoryAsync"/>); a year before the first
/// reading is counted from the reviews of the accounts that have one.</summary>
public interface IPlaytimeService
{
    /// <summary>What an owner played per calendar year (UTC), the earliest first, each year saying
    /// where it is counted from and the days it covers. Empty while nothing was read.</summary>
    Task<IReadOnlyList<PlayedYear>> GetYearsAsync(Guid ownerId, CancellationToken ct);

    /// <summary>The months an owner played a game in, the earliest first, each counted as its year is.
    /// A month with nothing played is left out: <see cref="GetYearsAsync"/> says which days are
    /// counted at all, and what was played of every game over them. Only the game's own readings are
    /// read. A game the owner does not own may have months: a review counts what was played.</summary>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownGame"/>: the catalogue has no such
    /// game.</exception>
    Task<IReadOnlyList<GameMonth>> GetGameMonthsAsync(Guid ownerId, Guid gameId, CancellationToken ct);

    /// <summary>What an owner played in the month <paramref name="month"/> is a day of, game by game,
    /// counted as the month's year is. A month nothing counts (before the first reading in a year with
    /// no review, or still to come) is answered with no source and no game.</summary>
    Task<LibraryMonth> GetMonthAsync(Guid ownerId, DateOnly month, CancellationToken ct);
}
