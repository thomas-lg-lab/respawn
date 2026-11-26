namespace Respawn.Games;

// One game as an owner sees it: the catalogue's record, what they own of it, and where it stands in
// their library. What they played of it over time is read apart (Playtime.cs).

/// <summary>A game as the catalogue has it: its canonical record and how each store knows it.</summary>
/// <param name="Id">The canonical record.</param>
/// <param name="Name">Null while no source has named the game.</param>
/// <param name="Stores">Its store entries, in no order; none for a record no store knows yet.</param>
public sealed record CatalogueGame(Guid Id, string? Name, IReadOnlyList<GameStore> Stores);

/// <summary>A game as one store knows it.</summary>
public sealed record GameStore(Guid StoreEntryId, string Source, string ExternalId);

/// <summary>Where a game stands among the games its owner launched, by total playtime.</summary>
/// <param name="Position">One for the most played; two games played as long share theirs.</param>
/// <param name="Launched">How many games the owner launched, this one included
/// (<see cref="LibraryStats.Launched"/>).</param>
public sealed record GameRank(int Position, int Launched);

/// <summary>One game, as one owner sees it.</summary>
/// <param name="Game">The catalogue's record.</param>
/// <param name="Owned">The game in the owner's library, as the library gives it; null when it is not
/// in it.</param>
/// <param name="Rank">Null when the owner never launched it.</param>
/// <param name="Removed">What the owner took out of their library of it; null when nothing was.</param>
public sealed record GameView(CatalogueGame Game, OwnedGame? Owned, GameRank? Rank, RemovedGame? Removed = null);
