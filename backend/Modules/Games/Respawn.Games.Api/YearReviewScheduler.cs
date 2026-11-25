using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Respawn.Games.Ports;

namespace Respawn.Games.Api;

/// <summary>
/// The import of the stores' yearly reviews, in this process, apart from the sync's schedule: every
/// little while it asks which linked accounts miss a review (<see cref="IYearReviewService.GetDueAsync"/>)
/// and has them imported one after the other. A newly linked account gets its past at the first turn;
/// the year just over comes in once the store has made its review.
///
/// Nothing here can stop the app, nor the sync: a turn that fails says so once, and the next one tries
/// again.
/// </summary>
internal sealed class YearReviewScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<YearReviewScheduler> logger)
    : BackgroundService
{
    /// <summary>How often the import looks for reviews to read. The first look comes one period after
    /// the app started.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromMinutes(15);

    /// <summary>Why the last look failed, to say it once and not at every turn.</summary>
    private string? _lastFailure;

    /// <summary>Why each account's last import failed, to say a trouble that lasts once and not every
    /// day. Turns run one after the other: no lock.</summary>
    private readonly Dictionary<Guid, string> _failing = [];

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

    /// <summary>One turn: every account with a review due, one after the other. Returns how many
    /// imports ended.</summary>
#pragma warning disable CA1031 // a turn that fails, whatever the reason, must not stop the import, the sync, nor the app
    internal async Task<int> RunDueAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> due;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            due = await scope.ServiceProvider.GetRequiredService<IYearReviewService>().GetDueAsync(ct);
            if (_lastFailure is not null)
            {
                logger.LogInformation("year in review: the import is back");
                _lastFailure = null;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var reason = ex.Message.Split('\n', 2)[0].Trim();
            if (reason != _lastFailure)
            {
                logger.LogWarning("year in review: nothing can be imported for now: {Reason}", reason);
                _lastFailure = reason;
            }

            return 0;
        }

        var ended = 0;
        foreach (var account in due)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<IYearReviewService>().ImportAsync(account, ct) is not { } import)
                {
                    continue;
                }

                ended++;
                if (import.Error is { } error)
                {
                    // Said when it starts or changes, not at each day's attempt.
                    var reason = $"{SyncWords.Of(error)}: {import.Detail}";
                    if (_failing.GetValueOrDefault(account) != reason)
                    {
                        logger.LogWarning(
                            "year in review: linked account {Account} could not be read ({Reason}); asked again each day",
                            account, reason);
                        _failing[account] = reason;
                    }
                }
                else if (_failing.Remove(account))
                {
                    logger.LogInformation("year in review: linked account {Account} is read again", account);
                }

                if (import.Imported.Count > 0)
                {
                    logger.LogInformation(
                        "year in review: linked account {Account}: {Years} imported", account, string.Join(", ", import.Imported));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "year in review: the import of linked account {Account} did not end", account);
            }
        }

        return ended;
    }
#pragma warning restore CA1031
}
