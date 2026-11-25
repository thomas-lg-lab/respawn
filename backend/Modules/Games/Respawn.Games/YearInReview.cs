namespace Respawn.Games;

// The past before the first reading: a store's own review of a year, when it keeps one (Steam's Year in
// Review). A month is its finest grain. It is imported once per year and linked account, apart from the
// daily sync, which never depends on it: the review comes from a call the store does not document, and
// may close without notice.

/// <summary>A store's review of one year of a linked account, as a connector read it.</summary>
/// <param name="Year">The year it reviews.</param>
/// <param name="Kind">Which of the source's answers this is, in the connector's words.</param>
/// <param name="TakenAt">When the source answered.</param>
/// <param name="Payload">The answer as received, kept whole: what the months do not use (streaks, first
/// sessions, the store's tags) can be read from it again later.</param>
/// <param name="Exists">False when the store has no review of that year: not made yet, or nothing to
/// review. Its months are then empty; once the year is settled
/// (<see cref="YearInReviewSchedule.Settled"/>), and when the store shows the account's library, it is
/// kept as a year with nothing played.</param>
/// <param name="Months">What was played, per month and per game. A game not played in a month is left
/// out of it.</param>
public sealed record YearReading(
    int Year, string Kind, DateTimeOffset TakenAt, string Payload, bool Exists, IReadOnlyList<MonthItem> Months);

/// <summary>One game in one month of a review, in the store's words.</summary>
/// <param name="ExternalId">The game's id in the store.</param>
/// <param name="Month">The first day of the month.</param>
/// <param name="Playtime">What was played that month, more than zero.</param>
/// <param name="Sessions">How many times it was launched that month, as the store counts.</param>
public sealed record MonthItem(string ExternalId, DateOnly Month, TimeSpan Playtime, int Sessions);

/// <summary>One game in one month, kept from a review: the store entry it is in the catalogue.</summary>
public sealed record MonthPlayed(Guid StoreEntryId, DateOnly Month, TimeSpan Playtime, int Sessions);

/// <summary>What an owner played of a game in one month, from the reviews of their linked accounts.</summary>
public sealed record PlayedMonth(Guid LinkedAccountId, DateOnly Month, Guid GameId, TimeSpan Playtime, int Sessions);

/// <summary>How the time of a year was counted.</summary>
public enum YearSource
{
    /// <summary>From the store's review: every month of the year, exact to the second.</summary>
    Review,

    /// <summary>From the daily readings: the growth of the totals between two in a row, from the day the
    /// library is read.</summary>
    Readings,
}

/// <summary>The time an owner played in one calendar year (UTC), and how much of the year that covers.</summary>
/// <param name="Year">The year.</param>
/// <param name="Playtime">What was played over <paramref name="From"/> to <paramref name="To"/>.</param>
/// <param name="Source">Where it was counted from.</param>
/// <param name="From">The first day counted.</param>
/// <param name="To">The last day counted: the year's last, or today.</param>
/// <param name="Whole">True when the whole year is counted: its first day to its last.</param>
public sealed record PlayedYear(int Year, TimeSpan Playtime, YearSource Source, DateOnly From, DateOnly To, bool Whole);

/// <summary>The rhythm of the import of the reviews.</summary>
public static class YearInReviewSchedule
{
    /// <summary>The first year a review exists for: nothing earlier was ever reviewed.</summary>
    public const int FirstYear = 2022;

    /// <summary>How long after an attempt the years still missing are asked again: a review of the last
    /// year appears around its end, and a store that did not answer is not asked at every turn.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromDays(1);

    /// <summary>From when a store with no review of a year is taken at its word, for an account whose
    /// library it shows: nothing was played that year on that account (it did not exist yet, or was not
    /// used). Steam makes its reviews in December;
    /// a month after the year ended, one that is still missing will not come. Before that, the year is
    /// asked again.</summary>
    public static DateTimeOffset Settled(int year) => new(year + 1, 2, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The years a review is asked for at that moment: from the first to the last one over.</summary>
    public static IEnumerable<int> Years(DateTimeOffset now)
    {
        var last = now.ToUniversalTime().Year - 1;
        for (var year = FirstYear; year <= last; year++)
        {
            yield return year;
        }
    }
}

/// <summary>The imports on their way, in this process, and when each linked account was last tried: one
/// import of an account at a time, and the years still missing not asked again before
/// <see cref="YearInReviewSchedule.RetryAfter"/>. A restart forgets it, and asks once more.</summary>
public sealed class YearInReviewGate
{
    private readonly HashSet<Guid> _running = [];
    private readonly Dictionary<Guid, DateTimeOffset> _tried = [];

    public bool TryEnter(Guid linkedAccountId)
    {
        lock (_running)
        {
            return _running.Add(linkedAccountId);
        }
    }

    public void Leave(Guid linkedAccountId)
    {
        lock (_running)
        {
            _running.Remove(linkedAccountId);
        }
    }

    public DateTimeOffset? TriedAt(Guid linkedAccountId)
    {
        lock (_running)
        {
            return _tried.TryGetValue(linkedAccountId, out var at) ? at : null;
        }
    }

    public void Tried(Guid linkedAccountId, DateTimeOffset at)
    {
        lock (_running)
        {
            _tried[linkedAccountId] = at;
        }
    }
}

/// <summary>How the import of one linked account's reviews ended.</summary>
/// <param name="Imported">The years imported by this run, those kept with nothing played included.</param>
/// <param name="Error">Why the store could not be read, when it could not: the years after the one that
/// failed are left for the next attempt.</param>
/// <param name="Detail">What happened, in the connector's words, when it failed. Never a secret.</param>
public sealed record YearImport(IReadOnlyList<int> Imported, SourceError? Error = null, string? Detail = null);
