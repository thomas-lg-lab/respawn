namespace Respawn.Games;

/// <summary>What a game database answered when asked which of its games some games of a store are: its
/// answers untouched, and the matches read from them.</summary>
/// <param name="Kind">Which of the database's answers this is: the name its snapshots are kept under.</param>
/// <param name="TakenAt">When the database gave its first answer.</param>
/// <param name="Payloads">Its answers as they came, in their order: one per question, a long answer taking
/// several. None when nothing was worth asking.</param>
/// <param name="Matches">The games of the store the database knows, in no order. A game it does not know is
/// left out; one it knows as several of its games is there once for each.</param>
public sealed record MatchReading(string Kind, DateTimeOffset TakenAt, IReadOnlyList<string> Payloads, IReadOnlyList<GameMatch> Matches);

/// <summary>A game of a store, as one game of a game database.</summary>
/// <param name="ExternalId">The game's id in the store.</param>
/// <param name="GameId">The game's id in the database.</param>
public sealed record GameMatch(string ExternalId, string GameId);
