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

/// <summary>What a game database knows of one of its games, as far as the catalogue keeps it: whatever
/// the answer did not say, or said in a shape that cannot be kept, is null or empty.</summary>
/// <param name="GameId">The game's id in the database.</param>
/// <param name="Name">Its name in the database.</param>
/// <param name="Slug">The short name of its page in the database.</param>
/// <param name="Url">The address of its page in the database, for a person to follow.</param>
/// <param name="CoverImageId">What names its cover in the database's image host.</param>
/// <param name="Genres">Its genres, in the database's words.</param>
/// <param name="Themes">Its themes, in the database's words.</param>
/// <param name="Summary">What it is about, in the database's words (IGDB: English).</param>
/// <param name="TimeToBeat">How long players say it takes; null when none said.</param>
public sealed record GameFacts(
    string GameId,
    string? Name,
    string? Slug,
    string? Url,
    string? CoverImageId,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Themes,
    string? Summary,
    TimeToBeat? TimeToBeat);

/// <summary>How long the players who said so took to finish a game, in seconds: rushing it, at a normal
/// pace, and doing everything. Each is null when nobody said.</summary>
/// <param name="Count">How many players the figures rest on; null when the database did not say.</param>
public sealed record TimeToBeat(int? Hastily, int? Normally, int? Completely, int? Count);

/// <summary>One answer of a source, untouched, to keep as a snapshot.</summary>
/// <param name="Kind">Which of the source's answers it is.</param>
/// <param name="TakenAt">When the source gave it.</param>
/// <param name="Payload">The answer as received.</param>
public sealed record SourceAnswer(string Kind, DateTimeOffset TakenAt, string Payload);

/// <summary>What a game database answered when asked about some of its games: its answers untouched,
/// and what was read from them.</summary>
/// <param name="Answers">Its answers as they came; none when nothing was worth asking.</param>
/// <param name="Games">The games it answered about, in no order. A game it did not answer about is left
/// out.</param>
public sealed record GamesReading(IReadOnlyList<SourceAnswer> Answers, IReadOnlyList<GameFacts> Games);

/// <summary>A store entry whose game the game database is to be asked about.</summary>
/// <param name="StoreEntryId">The store entry.</param>
/// <param name="ExternalId">The game's id in its store.</param>
public sealed record GameDataEntry(Guid StoreEntryId, string ExternalId);

/// <summary>Which game of the game database a store entry is: null when the database does not know it.</summary>
public sealed record GameDataMatch(Guid StoreEntryId, string? GameId);

/// <summary>When a store entry is due again: asked before <paramref name="MatchedBefore"/> when the
/// database knew it, before <paramref name="UnknownBefore"/> when it did not. One never asked always is.</summary>
public sealed record GameDataDue(DateTimeOffset MatchedBefore, DateTimeOffset UnknownBefore);

/// <summary>How asking the game database about a store's games ended.</summary>
/// <param name="Matched">The games it knew, kept by this run.</param>
/// <param name="Unknown">The games it did not know, kept as such by this run.</param>
/// <param name="Error">Why it could not be read, when it could not: the games left are asked about once
/// <see cref="GameDataSchedule.RetryAfter"/> has passed.</param>
/// <param name="Detail">What happened, in the connector's words.</param>
public sealed record GameDataRead(int Matched, int Unknown, SourceError? Error = null, string? Detail = null);

/// <summary>When the game database is asked about the catalogue's games. Apart from the sync: it only
/// needs the games a sync listed, and nothing of the sync waits for it. One record per game for the whole
/// instance: an account's games are asked about once, whoever owns them.</summary>
public static class GameDataSchedule
{
    /// <summary>How long what the database said of a game it knows is taken before it is asked again:
    /// durations, genres and descriptions change now and then.</summary>
    public static readonly TimeSpan AskAgainAfter = TimeSpan.FromDays(30);

    /// <summary>How long a game it did not know is left before it is asked again: a new game is often
    /// added to the database within days of its release.</summary>
    public static readonly TimeSpan UnknownAskAgainAfter = TimeSpan.FromDays(7);

    /// <summary>How long a store's games are left alone after the database could not be read: it is not
    /// asked again at every turn.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

    /// <summary>How many times in a row the same games may leave the database's answer unreadable before
    /// they are set aside as unknown, to be asked about again after <see cref="UnknownAskAgainAfter"/>:
    /// a batch whose answer cannot be read must not hold back every game after it.</summary>
    public const int UnreadableBeforeSetAside = 3;

    /// <summary>The most games asked about in one turn, unless the instance says otherwise: a library
    /// read for the first time is spread over several turns, never asked about all at once.</summary>
    public const int GamesPerTurn = 100;

    /// <summary>What is due at that moment.</summary>
    public static GameDataDue DueAt(DateTimeOffset now) => new(now - AskAgainAfter, now - UnknownAskAgainAfter);
}

/// <summary>How the instance asks the game database: how many games a turn takes at most.</summary>
public sealed record GameDataOptions(int GamesPerTurn = GameDataSchedule.GamesPerTurn);

/// <summary>The questions on their way, in this process, and when each store's games could last not be
/// asked about: one at a time per store, and not again before <see cref="GameDataSchedule.RetryAfter"/>.
/// A restart forgets it, and asks once more.</summary>
public sealed class GameDataGate
{
    private readonly HashSet<string> _running = [];
    private readonly Dictionary<string, (DateTimeOffset At, int Unreadable)> _failed = [];

    public bool TryEnter(string store)
    {
        lock (_running)
        {
            return _running.Add(store);
        }
    }

    public void Leave(string store)
    {
        lock (_running)
        {
            _running.Remove(store);
        }
    }

    /// <summary>When the database could last not be read about this store's games, while that has not
    /// been followed by a turn that went through.</summary>
    public DateTimeOffset? FailedAt(string store)
    {
        lock (_running)
        {
            return _failed.TryGetValue(store, out var failed) ? failed.At : null;
        }
    }

    /// <summary>Says the database could not be read about this store's games at that moment. Returns how
    /// many turns in a row ended with an answer that could not be read, this one included: 0 when this
    /// failure was of another kind.</summary>
    public int Failed(string store, DateTimeOffset at, bool unreadable)
    {
        lock (_running)
        {
            var count = unreadable ? _failed.GetValueOrDefault(store).Unreadable + 1 : 0;
            _failed[store] = (at, count);
            return count;
        }
    }

    /// <summary>Says the games that kept failing were set aside: the count starts again.</summary>
    public void SetAside(string store)
    {
        lock (_running)
        {
            if (_failed.TryGetValue(store, out var failed))
            {
                _failed[store] = (failed.At, 0);
            }
        }
    }

    public void Succeeded(string store)
    {
        lock (_running)
        {
            _failed.Remove(store);
        }
    }
}
