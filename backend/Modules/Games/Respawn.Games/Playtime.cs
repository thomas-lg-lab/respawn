namespace Respawn.Games;

// The time played, read back by month: of one game, or of a whole library in one month. A year is in
// YearInReview.cs (PlayedYear), and the three are counted by the same rule.

/// <summary>What an owner played of a game in one month.</summary>
/// <param name="Month">The first day of the month.</param>
/// <param name="Playtime">More than zero.</param>
/// <param name="Source">Where it was counted from, as its year is (<see cref="PlayedYear.Source"/>).</param>
public sealed record GameMonth(DateOnly Month, TimeSpan Playtime, YearSource Source);

/// <summary>A game's share of a month.</summary>
/// <param name="GameId">The canonical record.</param>
/// <param name="Name">Null while no source has named the game: a review names none.</param>
/// <param name="Playtime">More than zero.</param>
public sealed record MonthGame(Guid GameId, string? Name, TimeSpan Playtime);

/// <summary>What an owner played in one month, game by game.</summary>
/// <param name="Month">The first day of the month.</param>
/// <param name="Source">Where it was counted from, as the month's year is; null when nothing counts
/// that month: no review of its year, and no reading yet, or none any more.</param>
/// <param name="From">The first day counted; null with no source.</param>
/// <param name="To">The last day counted; null with no source.</param>
/// <param name="Games">The games played, the most played first.</param>
public sealed record LibraryMonth(
    DateOnly Month, YearSource? Source, DateOnly? From, DateOnly? To, IReadOnlyList<MonthGame> Games);
