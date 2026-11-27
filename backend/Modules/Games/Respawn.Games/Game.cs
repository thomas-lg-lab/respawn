namespace Respawn.Games;

// One game as an owner sees it: the catalogue's record, what they own of it, and where it stands in
// their library. What they played of it over time is read apart (Playtime.cs).

/// <summary>A game as the catalogue has it: its canonical record and how each store knows it.</summary>
/// <param name="Id">The canonical record.</param>
/// <param name="Name">Null while no source has named the game.</param>
/// <param name="Stores">Its store entries, in no order; none for a record no store knows yet.</param>
/// <param name="GameData">Its page in the game database, when the database knows it and gave one.</param>
public sealed record CatalogueGame(Guid Id, string? Name, IReadOnlyList<GameStore> Stores, GameDataPage? GameData = null);

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

/// <summary>A game's page in the game database, for a person to follow.</summary>
/// <param name="Source">The database, as the catalogue names it.</param>
/// <param name="Url">The page.</param>
public sealed record GameDataPage(string Source, string Url);

/// <summary>A source whose data the instance shows, to be credited where its users can see it.</summary>
/// <param name="Source">The source, as the catalogue names it.</param>
/// <param name="Label">Its name, as people know it.</param>
/// <param name="Url">Its home page.</param>
public sealed record Credit(string Source, string Label, Uri Url);
