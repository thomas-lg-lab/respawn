using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Respawn.Games.Api;

/// <summary>How one linked account's turn ended, in the words the log says it with.</summary>
/// <param name="Done">What it brought, when that is worth a line.</param>
/// <param name="Trouble">Why the store could not be read, when it could not.</param>
internal sealed record Turn(string? Done = null, Trouble? Trouble = null);

/// <summary>Why a store could not be read: the kind of failure, and what happened. Never a secret.</summary>
internal sealed record Trouble(string Error, string? Detail);

/// <summary>
/// A schedule in this process: every period it asks which linked accounts are due, and takes them one
/// after the other. What is due, and when, is the core's to say; this only keeps the rhythm. The app
/// runs as one replica.
///
/// Nothing here can stop the app: a turn that fails says so and the next one tries again. A trouble
/// that lasts (no database, a key refused) is said once, when it starts or changes, and once more when
/// it is over.
/// </summary>
/// <param name="scopes">A scope per look and per account, as a request would have.</param>
/// <param name="clock">The app's clock, which a test sets.</param>
/// <param name="logger">Where the schedule says what it did.</param>
/// <param name="period">How often it looks for what is due. The first look comes one period after the
/// app started.</param>
/// <param name="name">What its lines in the log start with.</param>
internal abstract class DueScheduler(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger logger, TimeSpan period, string name) : BackgroundService
{
    /// <summary>Why the last look failed, to say it once and not at every turn.</summary>
    private string? _lastFailure;

    /// <summary>Why each account's last turn failed. Turns run one after the other: no lock.</summary>
    private readonly Dictionary<Guid, Trouble> _failing = [];

    /// <summary>The linked accounts that are due, in the order to take them.</summary>
    protected abstract Task<IReadOnlyList<Guid>> DueAsync(IServiceProvider services, CancellationToken ct);

    /// <summary>One account's turn. Null when there was nothing to do after all.</summary>
    protected abstract Task<Turn?> RunAsync(IServiceProvider services, Guid account, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(period, clock);
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

    /// <summary>One turn of the schedule: every account that is due, one after the other. Returns how
    /// many turns ended.</summary>
#pragma warning disable CA1031 // a turn that fails, whatever the reason, must not stop the schedule, nor the app
    internal async Task<int> RunDueAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> due;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            due = await DueAsync(scope.ServiceProvider, ct);
            if (_lastFailure is not null)
            {
                logger.LogInformation("{Schedule}: the schedule is back", name);
                _lastFailure = null;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Most often the database is not there yet.
            var reason = FirstLine(ex);
            if (reason != _lastFailure)
            {
                logger.LogWarning("{Schedule}: nothing can be scheduled for now: {Reason}", name, reason);
                _lastFailure = reason;
            }

            return 0;
        }

        var ended = 0;
        foreach (var account in due)
        {
            try
            {
                // A scope each: one account's trouble is not the next one's.
                await using var scope = scopes.CreateAsyncScope();
                if (await RunAsync(scope.ServiceProvider, account, ct) is not { } turn)
                {
                    continue;
                }

                ended++;
                Say(account, turn);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Schedule}: the turn of linked account {Account} did not end", name, account);
            }
        }

        return ended;
    }
#pragma warning restore CA1031

    private void Say(Guid account, Turn turn)
    {
        if (turn.Trouble is { } trouble)
        {
            if (_failing.GetValueOrDefault(account) != trouble)
            {
                logger.LogWarning(
                    "{Schedule}: linked account {Account} could not be read ({Error}): {Detail}; not said again while it lasts",
                    name, account, trouble.Error, trouble.Detail);
                _failing[account] = trouble;
            }
        }
        else if (_failing.Remove(account))
        {
            logger.LogInformation("{Schedule}: linked account {Account} is read again", name, account);
        }

        if (turn.Done is not null)
        {
            logger.LogInformation("{Schedule}: linked account {Account}: {Done}", name, account, turn.Done);
        }
    }

    private static string FirstLine(Exception exception)
    {
        var line = exception.Message.Split('\n', 2)[0].Trim();
        return line.Length > 0 ? line : exception.GetType().Name;
    }
}
