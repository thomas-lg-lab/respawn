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

/// <summary>One sync of one linked account, once it has ended.</summary>
/// <param name="Id">The run.</param>
/// <param name="LinkedAccountId">The store account that was read.</param>
/// <param name="Trigger">Why it ran.</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="FinishedAt">When it ended.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Error">Why it failed; null unless it did.</param>
/// <param name="Detail">What happened, in the connector's words, when it failed. Never a secret.</param>
/// <param name="SnapshotId">The store's answer, when there was one.</param>
public sealed record SyncRun(
    Guid Id,
    Guid LinkedAccountId,
    SyncTrigger Trigger,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    SyncOutcome Outcome,
    SourceError? Error = null,
    string? Detail = null,
    Guid? SnapshotId = null);

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

/// <summary>The syncs on their way, in this process: a linked account is read by one at a time. One
/// for the whole app, which runs as one replica.</summary>
public sealed class SyncGate
{
    private readonly HashSet<Guid> _running = [];

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
}
