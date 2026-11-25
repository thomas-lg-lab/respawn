using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Respawn.Games.Ports;

namespace Respawn.Games.Api;

/// <summary>
/// The schedule of the sync: every few minutes it asks which linked accounts are due their reading of
/// the day, and has them read one after the other. What is due, and when a failed reading is made
/// again, is the core's to say (<see cref="ISyncService.GetDueAsync"/>); the runs are kept in the
/// database, so a restart starts nothing twice.
/// </summary>
internal sealed class SyncScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<SyncScheduler> logger)
    : DueScheduler(scopes, clock, logger, Period, "library sync")
{
    public static readonly TimeSpan Period = TimeSpan.FromMinutes(5);

    protected override Task<IReadOnlyList<Guid>> DueAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<ISyncService>().GetDueAsync(ct);

    protected override async Task<Turn?> RunAsync(IServiceProvider services, Guid account, CancellationToken ct) =>
        await services.GetRequiredService<ISyncService>().RunScheduledAsync(account, ct) switch
        {
            null => null,
            { Outcome: SyncOutcome.Failed } run =>
                new Turn(Trouble: new Trouble(SyncWords.Of(run.Error ?? SourceError.Unreadable), run.Detail)),
            var run => new Turn(Done: SyncWords.Of(run.Outcome)),
        };
}
