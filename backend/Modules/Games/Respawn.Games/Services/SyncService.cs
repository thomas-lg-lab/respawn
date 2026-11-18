using Respawn.Games.Ports;

namespace Respawn.Games.Services;

/// <summary>The sync: a connector reads a linked account's library, <see cref="ILibraryService"/> records
/// it, and the run is kept with its outcome. Nothing is tried twice here: the schedule decides when a
/// failed reading is made again.</summary>
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

        // A button pressed again and again asks the store nothing more.
        if (await runs.FindLatestAsync(account.Id, ct) is { } latest
            && latest.StartedAt > clock.GetUtcNow() - SyncSchedule.ManualPause)
        {
            throw new GamesException(GamesError.SyncTooSoon);
        }

        return await RunAsync(account, SyncTrigger.Manual, ct) ?? throw new GamesException(GamesError.SyncInProgress);
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
        if (await linkedAccounts.FindByIdAsync(linkedAccountId, ct) is not { } account)
        {
            return null;
        }

        try
        {
            return await RunAsync(account, SyncTrigger.Scheduled, ct);
        }
        catch (GamesException ex) when (ex.Error == GamesError.UnknownLinkedAccount)
        {
            // Unlinked while it was read: nothing of it is kept, the run included.
            return null;
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

    /// <summary>One sync, from the store's answer to the run kept. Null when the account is already
    /// being read.</summary>
    private async Task<SyncRun?> RunAsync(LinkedAccount account, SyncTrigger trigger, CancellationToken ct)
    {
        if (!gate.TryEnter(account.Id))
        {
            return null;
        }

        try
        {
            var startedAt = clock.GetUtcNow();
            SyncRun Ended(SyncOutcome outcome, SourceError? error = null, string? detail = null, Guid? snapshot = null) =>
                new(Guid.CreateVersion7(), account.Id, trigger, startedAt, clock.GetUtcNow(), outcome, error, Cut(detail), snapshot);

            SyncRun run;
            try
            {
                if (sources.FirstOrDefault(source => source.Source == account.Source) is not { } source)
                {
                    throw new SourceException(SourceError.NotConfigured, $"the instance has no connector for {account.Source}");
                }

                // With the instance's access: nobody gives a key of their own yet.
                var reading = await source.ReadLibraryAsync(account.ExternalId, credential: null, ct);
                var snapshot = await library.RecordLibraryAsync(account.Id, reading, daily: trigger == SyncTrigger.Scheduled, ct);
                run = Ended(reading.Items.Count > 0 ? SyncOutcome.Succeeded : SyncOutcome.NothingVisible, snapshot: snapshot);
            }
            catch (SourceException ex)
            {
                run = Ended(SyncOutcome.Failed, ex.Error, ex.Message);
            }
            catch (ArgumentException ex)
            {
                // An account id the connector does not take, or an answer the catalogue refuses: said
                // once and kept, or the schedule would ask the store again at every turn.
                run = Ended(SyncOutcome.Failed, SourceError.Unreadable, "the reading could not be recorded: " + ex.Message);
            }

            return await unitOfWork.RunAsync(
                async () =>
                {
                    // Under the lock unlinking takes: the account is there, or the run is not kept.
                    if (await linkedAccounts.FindByIdAsync(account.Id, ct) is null)
                    {
                        throw new GamesException(GamesError.UnknownLinkedAccount);
                    }

                    await runs.AddAsync(run, ct);
                    return run;
                },
                ct);
        }
        finally
        {
            gate.Leave(account.Id);
        }
    }

    private static string? Cut(string? detail) =>
        detail is { Length: > Limits.SyncDetail } ? detail[..Limits.SyncDetail] : detail;
}
