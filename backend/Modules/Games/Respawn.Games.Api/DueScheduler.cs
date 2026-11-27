using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Respawn.SharedKernel;

namespace Respawn.Games.Api;

/// <summary>How one turn ended, in the words the log says it with.</summary>
/// <param name="Done">What it brought, when that is worth a line.</param>
/// <param name="Trouble">Why the source could not be read, when it could not.</param>
internal sealed record Turn(string? Done = null, Trouble? Trouble = null);

/// <summary>Why a source could not be read: the kind of failure, and what happened. Never a secret.</summary>
internal sealed record Trouble(string Error, string? Detail);

/// <summary>
/// A schedule in this process: every period it asks what is due, and takes it one after the other: a
/// linked account, or a store whose games are to be asked about. What is due, and when, is the core's to
/// say; this only keeps the rhythm. The app runs as one replica.
///
/// Nothing here can stop the app: a turn that fails says so and the next one tries again. A trouble
/// that lasts (no database, a key refused) is said once, when it starts or changes, and once more when
/// it is over. A look made while another caller is connecting to the database is skipped in silence:
/// it is no trouble.
/// </summary>
/// <param name="scopes">A scope per look and per turn, as a request would have.</param>
/// <param name="clock">The app's clock, which a test sets.</param>
/// <param name="logger">Where the schedule says what it did.</param>
/// <param name="period">How often it looks for what is due. The first look comes one period after the
/// app started.</param>
/// <param name="name">What its lines in the log start with.</param>
internal abstract class DueScheduler<TKey>(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger logger, TimeSpan period, string name) : BackgroundService
    where TKey : notnull
{
    /// <summary>Why the last look failed, to say it once and not at every turn.</summary>
    private string? _lastFailure;

    /// <summary>Why each one's last turn failed. Turns run one after the other: no lock.</summary>
    private readonly Dictionary<TKey, Trouble> _failing = [];

    /// <summary>What is due, in the order to take it.</summary>
    protected abstract Task<IReadOnlyList<TKey>> DueAsync(IServiceProvider services, CancellationToken ct);

    /// <summary>One turn. Null when there was nothing to do after all.</summary>
    protected abstract Task<Turn?> RunAsync(IServiceProvider services, TKey key, CancellationToken ct);

    /// <summary>What a turn is about, as the log says it: "linked account ...".</summary>
    protected abstract string Subject(TKey key);

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

    /// <summary>One turn of the schedule: everything that is due, one after the other. Returns how
    /// many turns ended.</summary>
#pragma warning disable CA1031 // a turn that fails, whatever the reason, must not stop the schedule, nor the app
    internal async Task<int> RunDueAsync(CancellationToken ct)
    {
        IReadOnlyList<TKey> due;
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
        catch (Exception ex) when (DatabaseUnavailableException.OnlyBusy(ex))
        {
            // Another schedule or a request is opening the database: the schedules all look at the same
            // moment after the start, and one of them connects. Nothing is wrong, so nothing is said, and
            // a trouble said before is neither repeated nor declared over: the next look tells.
            logger.LogDebug("{Schedule}: skipped a look, the database is being connected to", name);
            return 0;
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
        foreach (var key in due)
        {
            try
            {
                // A scope each: one turn's trouble is not the next one's.
                await using var scope = scopes.CreateAsyncScope();
                if (await RunAsync(scope.ServiceProvider, key, ct) is not { } turn)
                {
                    continue;
                }

                ended++;
                Say(key, turn);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Schedule}: the turn of {Subject} did not end", name, Subject(key));
            }
        }

        return ended;
    }
#pragma warning restore CA1031

    private void Say(TKey key, Turn turn)
    {
        if (turn.Trouble is { } trouble)
        {
            if (_failing.GetValueOrDefault(key) != trouble)
            {
                logger.LogWarning(
                    "{Schedule}: {Subject} could not be read ({Error}): {Detail}; not said again while it lasts",
                    name, Subject(key), trouble.Error, trouble.Detail);
                _failing[key] = trouble;
            }
        }
        else if (_failing.Remove(key))
        {
            logger.LogInformation("{Schedule}: {Subject} is read again", name, Subject(key));
        }

        if (turn.Done is not null)
        {
            logger.LogInformation("{Schedule}: {Subject}: {Done}", name, Subject(key), turn.Done);
        }
    }

    private static string FirstLine(Exception exception)
    {
        var line = exception.Message.Split('\n', 2)[0].Trim();
        return line.Length > 0 ? line : exception.GetType().Name;
    }
}

/// <summary>A schedule whose turns are linked accounts, one after the other.</summary>
internal abstract class DueScheduler(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger logger, TimeSpan period, string name)
    : DueScheduler<Guid>(scopes, clock, logger, period, name)
{
    protected override string Subject(Guid key) => $"linked account {key}";
}
