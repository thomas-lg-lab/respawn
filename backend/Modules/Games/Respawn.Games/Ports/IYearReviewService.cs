namespace Respawn.Games.Ports;

/// <summary>The import of the past from a store's yearly reviews (<see cref="IYearReviewSource"/>).
/// Apart from the daily sync: whatever happens here, the sync goes on. What it lets an owner see is read
/// by <see cref="IPlaytimeService"/>.</summary>
public interface IYearReviewService
{
    /// <summary>The linked accounts with a review still to import, the earliest link first: those a
    /// connector can read the reviews of, missing at least one year from
    /// <see cref="YearInReviewSchedule.FirstYear"/> to the last one over, and not tried less than
    /// <see cref="YearInReviewSchedule.RetryAfter"/> ago in this process. A newly linked account is due
    /// once its first sync ended.</summary>
    Task<IReadOnlyList<Guid>> GetDueAsync(CancellationToken ct);

    /// <summary>Imports the reviews a linked account is missing, the earliest year first, each in its own
    /// transaction: the answer as a snapshot, and what it says per month and per game. A year the store
    /// has no review of is left for the next attempt, until the year is settled
    /// (<see cref="YearInReviewSchedule.Settled"/>) and the last sync read the account's library with the
    /// same access: it is then kept as a year with nothing played. A store that cannot be read stops
    /// the run there.
    /// Null when there is nothing to do: the account is gone, is being imported right now, or was tried
    /// less than <see cref="YearInReviewSchedule.RetryAfter"/> ago.</summary>
    Task<YearImport?> ImportAsync(Guid linkedAccountId, CancellationToken ct);
}
