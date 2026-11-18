using Respawn.Games.Ports;

namespace Respawn.Games.Services;

/// <summary>The sync: a connector reads a linked account's library, <see cref="ILibraryService"/> records
/// it, and the run is kept with its outcome, in one transaction with what was recorded. Nothing is tried
/// twice here: the schedule decides when a failed reading is made again.</summary>
public sealed class SyncService(
    IUnitOfWork unitOfWork,
    ILinkedAccountStore linkedAccounts,
    ISyncRunStore runs,
    ILibraryService library,
    IEnumerable<ILibrarySource> sources,
    SyncGate gate,
    TimeProvider clock) : ISyncService
{
    public async Task<SyncRun> SyncNowAsync(Guid ownerId, Guid linkedAccountId, CancellationToken ct)
    {
        if (await linkedAccounts.FindByIdAsync(linkedAccountId, ct) is not { } account || account.OwnerId != ownerId)
        {
            throw new GamesException(GamesError.UnknownLinkedAccount);
        }

        if (!gate.TryEnter(account.Id))
        {
            throw new GamesException(GamesError.SyncInProgress);
        }

        try
        {
            // A button pressed again and again asks the store nothing more. Looked at once nothing
            // else reads the account: the run that just ended is seen. The last start is also
            // remembered here, for the sync that left no run behind.
            var now = clock.GetUtcNow();
            var earliest = now - SyncSchedule.ManualPause;
            if ((await runs.FindLatestAsync(account.Id, ct))?.StartedAt > earliest
                || gate.StartedAt(account.Id, SyncTrigger.Manual) > earliest)
            {
                throw new GamesException(GamesError.SyncTooSoon);
            }

            gate.Started(account.Id, SyncTrigger.Manual, now);
            return await RunAsync(account, SyncTrigger.Manual, now, ct);
        }
        finally
        {
            gate.Leave(account.Id);
        }
    }

    public async Task<IReadOnlyList<Guid>> GetDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = (await runs.ListSinceAsync(SyncSchedule.DayOf(now), ct))
            .Where(run => run.Trigger == SyncTrigger.Scheduled)
            .ToLookup(run => run.LinkedAccountId);
        var readable = sources.Select(source => source.Source).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. (await linkedAccounts.ListAllAsync(ct))
                .Where(account => readable.Contains(account.Source) && Due([.. today[account.Id]], now))
                .OrderBy(account => account.LinkedAt)
                .ThenBy(account => account.Id)
                .Select(account => account.Id),
        ];
    }

    public async Task<SyncRun?> RunScheduledAsync(Guid linkedAccountId, CancellationToken ct)
    {
        if (await linkedAccounts.FindByIdAsync(linkedAccountId, ct) is not { } account || !gate.TryEnter(account.Id))
        {
            return null;
        }

        try
        {
            // An account is due until a run says otherwise. When the last reading left no run (it
            // could not be kept, or was given up), the store is not asked again at every turn.
            var now = clock.GetUtcNow();
            if (gate.StartedAt(account.Id, SyncTrigger.Scheduled) > now - SyncSchedule.RetryAfter)
            {
                return null;
            }

            gate.Started(account.Id, SyncTrigger.Scheduled, now);
            return await RunAsync(account, SyncTrigger.Scheduled, now, ct);
        }
        catch (GamesException ex) when (ex.Error == GamesError.UnknownLinkedAccount)
        {
            // Unlinked while it was read: nothing of it is kept, the run included.
            return null;
        }
        finally
        {
            gate.Leave(account.Id);
        }
    }

    /// <summary>Whether the reading of the day is still to make, from the scheduled runs of the day.</summary>
    private static bool Due(SyncRun[] today, DateTimeOffset now)
    {
        if (today.Length == 0)
        {
            return true;
        }

        if (today.Any(run => run.Outcome != SyncOutcome.Failed))
        {
            return false;
        }

        var last = today.MaxBy(run => run.StartedAt)!;
        return last.Error == SourceError.Unavailable && last.FinishedAt + SyncSchedule.RetryAfter <= now;
    }

    /// <summary>One sync, from the store's answer to the run kept. The caller holds the gate.</summary>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownLinkedAccount"/>: unlinked
    /// meanwhile; nothing is kept.</exception>
    private async Task<SyncRun> RunAsync(LinkedAccount account, SyncTrigger trigger, DateTimeOffset startedAt, CancellationToken ct)
    {
        SyncRun Ended(SyncOutcome outcome, SourceError? error = null, string? detail = null, Guid? snapshot = null) =>
            new(Guid.CreateVersion7(), account.Id, trigger, startedAt, clock.GetUtcNow(), outcome, error, Cut(detail), snapshot);

        // The store first, outside any transaction.
        LibraryReading? reading = null;
        SyncRun? failed = null;
        try
        {
            if (sources.FirstOrDefault(source => source.Source == account.Source) is not { } source)
            {
                throw new SourceException(SourceError.NotConfigured, $"the instance has no connector for {account.Source}");
            }

            // With the instance's access: nobody gives a key of their own yet.
            reading = await source.ReadLibraryAsync(account.ExternalId, credential: null, ct);
        }
        catch (SourceException ex)
        {
            failed = Ended(SyncOutcome.Failed, ex.Error, ex.Message);
        }
        catch (ArgumentException ex)
        {
            failed = Ended(SyncOutcome.Failed, SourceError.Unreadable, "the connector does not take this account: " + ex.Message);
        }

        if (reading is not null)
        {
            try
            {
                // The answer, what it becomes and the run, together or not at all: a reading kept
                // without its run would be made again, and a run without its reading would lie.
                return await KeepAsync(account, async () =>
                {
                    var snapshot = await library.RecordLibraryAsync(account.Id, reading, ct);
                    return Ended(reading.Items.Count > 0 ? SyncOutcome.Succeeded : SyncOutcome.NothingVisible, snapshot: snapshot);
                }, ct);
            }
            catch (ArgumentException ex)
            {
                // An answer the catalogue refuses. Said once and kept as a failure, or the schedule
                // would ask the store again at every turn.
                failed = Ended(SyncOutcome.Failed, SourceError.Unreadable, "the reading could not be recorded: " + ex.Message);
            }
        }

        return await KeepAsync(account, () => Task.FromResult(failed!), ct);
    }

    private Task<SyncRun> KeepAsync(LinkedAccount account, Func<Task<SyncRun>> ended, CancellationToken ct) =>
        unitOfWork.RunAsync(
            async () =>
            {
                // Under the lock unlinking takes: the account is there, or nothing is kept.
                if (await linkedAccounts.FindByIdAsync(account.Id, ct) is null)
                {
                    throw new GamesException(GamesError.UnknownLinkedAccount);
                }

                var run = await ended();
                await runs.AddAsync(run, ct);
                return run;
            },
            ct);

    private static string? Cut(string? detail) =>
        detail is { Length: > Limits.SyncDetail } ? detail[..Limits.SyncDetail] : detail;
}
