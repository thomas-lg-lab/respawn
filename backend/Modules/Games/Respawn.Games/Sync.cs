namespace Respawn.Games;

// The words of the sync: a linked account's library read from its store and recorded, once a day by
// the schedule, or when its owner asks.

/// <summary>Why a library was read.</summary>
public enum SyncTrigger
{
    /// <summary>The reading of the day, made by the schedule.</summary>
    Scheduled,

    /// <summary>Its owner asked for it.</summary>
    Manual,
}

/// <summary>How a sync ended.</summary>
public enum SyncOutcome
{
    /// <summary>The store answered a library, and it was recorded.</summary>
    Succeeded,

    /// <summary>The store answered, and showed no game: a profile made private looks like this, and so
    /// does an account that owns nothing. The answer is kept and the library is left as it was. Not a
    /// success: nothing was learnt, and the last good reading keeps ageing.</summary>
    NothingVisible,

    /// <summary>The store could not be read, or its answer could not be recorded.</summary>
    Failed,
}

/// <summary>One sync of one linked account, once it has ended. Kept in the same transaction as what
/// it recorded: a reading is never there without its run.</summary>
/// <param name="Id">The run.</param>
/// <param name="LinkedAccountId">The store account that was read.</param>
/// <param name="Trigger">Why it ran.</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="FinishedAt">When it ended.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Error">Why it failed; null unless it did.</param>
/// <param name="Detail">What happened, in the connector's words, when it failed. Never a secret.</param>
/// <param name="SnapshotId">The store's answer, when there was one.</param>
/// <param name="OwnCredential">True when the account was read, or was to be read, with the access its
/// owner gave on it rather than the instance's: a refusal is then theirs to fix.</param>
public sealed record SyncRun(
    Guid Id,
    Guid LinkedAccountId,
    SyncTrigger Trigger,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    SyncOutcome Outcome,
    SourceError? Error = null,
    string? Detail = null,
    Guid? SnapshotId = null,
    bool OwnCredential = false);

/// <summary>Where the sync of one linked account stands, as its owner's screen tells it.</summary>
/// <param name="Running">It is being read right now, by the schedule or at its owner's request.</param>
/// <param name="Last">The last run that ended, whatever its outcome (the latest started first); null
/// while none did. A run on its way is not one yet: while <paramref name="Running"/>, this is the one
/// before.</param>
/// <param name="LastSucceededAt">When the last run that recorded a library ended: how fresh the library
/// is. A run that saw nothing (<see cref="SyncOutcome.NothingVisible"/>) is not one. Null while none did.</param>
public sealed record SyncState(bool Running, SyncRun? Last, DateTimeOffset? LastSucceededAt)
{
    /// <summary>An account never read, and not being read.</summary>
    public static readonly SyncState Never = new(false, null, null);
}

/// <summary>The last run of a linked account, and the last one that succeeded; null while there is none.</summary>
public sealed record LatestRuns(SyncRun? Last, SyncRun? LastSucceeded);

/// <summary>The rhythm of the sync.</summary>
public static class SyncSchedule
{
    /// <summary>When a day of the sync starts, in UTC: the small hours in Europe, so that a day's
    /// reading holds the evening before it.</summary>
    public static readonly TimeSpan DayStartsAt = TimeSpan.FromHours(4);

    /// <summary>How long after a store did not answer the day's reading is tried again. Any other
    /// failure (no key, a key refused, an answer that cannot be read) waits for the next day, or for
    /// its owner to ask: trying again would say the same.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

    /// <summary>The least time between a sync and the next one its owner asks for.</summary>
    public static readonly TimeSpan ManualPause = TimeSpan.FromMinutes(1);

    /// <summary>The start of the sync's day a moment belongs to.</summary>
    public static DateTimeOffset DayOf(DateTimeOffset moment)
    {
        var shifted = moment.ToUniversalTime() - DayStartsAt;
        return new DateTimeOffset(shifted.Year, shifted.Month, shifted.Day, 0, 0, 0, TimeSpan.Zero) + DayStartsAt;
    }
}

/// <summary>The syncs on their way, in this process, and when the last ones started: a linked account
/// is read by one at a time, and not again at once when a sync left no run behind (a request given up,
/// a run that could not be kept). One for the whole app, which runs as one replica; a restart forgets
/// it, and the runs kept in the database still say most of it.</summary>
/// <remarks>What is remembered of a try is the account and why it was read: a sync its owner asked
/// for and the reading of the day do not hold each other back.</remarks>
public sealed class SyncGate : Gate<(Guid LinkedAccountId, SyncTrigger Trigger)>;
