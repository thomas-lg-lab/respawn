using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Respawn.Games.Ports;

namespace Respawn.Games.Api;

/// <summary>
/// The schedule of the sync, in this process: every few minutes it asks which linked accounts are due
/// their reading of the day, and has them read one after the other. What is due, and when a failed
/// reading is made again, is the core's to say (<see cref="ISyncService.GetDueAsync"/>); the runs are
/// kept in the database, so a restart starts nothing twice. The app runs as one replica.
///
/// Nothing here can stop the app: with no database, or no key, a turn says so once and the next one
/// tries again.
/// </summary>
internal sealed class SyncScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<SyncScheduler> logger)
    : BackgroundService
{
    /// <summary>How often the schedule looks for readings that are due. The first look comes one
    /// period after the app started.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromMinutes(5);

    /// <summary>Why the last look failed, to say it once and not at every turn.</summary>
    private string? _lastFailure;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunDueAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The app is stopping.
        }
    }

    /// <summary>One turn of the schedule: every reading that is due, one account after the other.
    /// Returns how many syncs ended.</summary>
#pragma warning disable CA1031 // a turn that fails, whatever the reason, must not stop the schedule, nor the app
    internal async Task<int> RunDueAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> due;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            due = await scope.ServiceProvider.GetRequiredService<ISyncService>().GetDueAsync(ct);
            if (_lastFailure is not null)
            {
                logger.LogInformation("library sync: the schedule is back");
                _lastFailure = null;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Most often the database is not there yet. Said when it starts, not every five minutes.
            var reason = FirstLine(ex);
            if (reason != _lastFailure)
            {
                logger.LogWarning("library sync: nothing can be scheduled for now: {Reason}", reason);
                _lastFailure = reason;
            }

            return 0;
        }

        var ended = 0;
        foreach (var account in due)
        {
            try
            {
                // A scope each, as a request would have: one account's trouble is not the next one's.
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<ISyncService>().RunScheduledAsync(account, ct) is not { } run)
                {
                    continue;
                }

                ended++;
                if (run.Outcome == SyncOutcome.Failed)
                {
                    logger.LogWarning(
                        "library sync: linked account {Account} could not be read ({Error}): {Detail}",
                        account, SyncWords.Of(run.Error ?? SourceError.Unreadable), run.Detail);
                }
                else
                {
                    logger.LogInformation("library sync: linked account {Account}: {Outcome}", account, SyncWords.Of(run.Outcome));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "library sync: the reading of linked account {Account} did not end", account);
            }
        }

        return ended;
    }
#pragma warning restore CA1031

    private static string FirstLine(Exception exception)
    {
        var line = exception.Message.Split('\n', 2)[0].Trim();
        return line.Length > 0 ? line : exception.GetType().Name;
    }
}
