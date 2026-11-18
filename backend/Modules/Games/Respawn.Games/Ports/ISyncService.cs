namespace Respawn.Games.Ports;

/// <summary>The sync use cases: reading a linked account's library from its store and recording it.
/// Every run that ends is kept, whatever its outcome, in the same transaction as what it recorded. A
/// run given up on the way (the app is stopping) keeps nothing.</summary>
public interface ISyncService
{
    /// <summary>Reads a linked account's library now, at its owner's request. A store that cannot be
    /// read is not a refusal: the run is returned, failed. Give it a token that outlives the request:
    /// a sync started is a sync to finish, whether or not its owner still waits for the answer.</summary>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownLinkedAccount"/>: no such linked
    /// account, or not this owner's. <see cref="GamesError.SyncInProgress"/>: it is being read right
    /// now. <see cref="GamesError.SyncTooSoon"/>: it was read less than
    /// <see cref="SyncSchedule.ManualPause"/> ago.</exception>
    Task<SyncRun> SyncNowAsync(Guid ownerId, Guid linkedAccountId, CancellationToken ct);

    /// <summary>Where the sync of each of these linked accounts stands, by linked account: the accounts
    /// <see cref="ILibraryService.GetLinkedAccountsAsync"/> answered their owner. One gone since is left out.</summary>
    Task<IReadOnlyDictionary<Guid, SyncState>> GetStatesAsync(IReadOnlyCollection<LinkedAccount> accounts, CancellationToken ct);

    /// <summary>The linked accounts whose reading of the day is due, the earliest link first: those a
    /// connector can read, with no scheduled run that reached the store since the day started
    /// (<see cref="SyncSchedule.DayOf"/>), and not waiting after a failure
    /// (<see cref="SyncSchedule.RetryAfter"/>). A newly linked account is due at once.</summary>
    Task<IReadOnlyList<Guid>> GetDueAsync(CancellationToken ct);

    /// <summary>Makes a linked account's reading of the day. Null when there is nothing to do: the
    /// account is gone, is being read right now, or a scheduled reading of it started less than
    /// <see cref="SyncSchedule.RetryAfter"/> ago in this process and left no run (a reading
    /// whose run could not be kept is not made again at every turn).</summary>
    Task<SyncRun?> RunScheduledAsync(Guid linkedAccountId, CancellationToken ct);
}
