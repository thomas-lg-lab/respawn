using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Respawn.Games.Ports;

namespace Respawn.Games.Api;

/// <summary>
/// Where the stores say the covers are, apart from the sync's schedule: every few minutes it asks which
/// linked accounts own games whose store is to ask (<see cref="ICoverService.GetDueAsync"/>) and has
/// them asked one after the other. A newly synced library gets its covers' places at the next turn; a
/// store that cannot be read is said once and tried again later. Whatever happens here, the sync goes
/// on, and a cover is still looked for where the connector can.
/// </summary>
internal sealed class CoverScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<CoverScheduler> logger)
    : DueScheduler(scopes, clock, logger, Period, "covers")
{
    public static readonly TimeSpan Period = TimeSpan.FromMinutes(5);

    protected override Task<IReadOnlyList<Guid>> DueAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<ICoverService>().GetDueAsync(ct);

    protected override async Task<Turn?> RunAsync(IServiceProvider services, Guid account, CancellationToken ct) =>
        await services.GetRequiredService<ICoverService>().ReadCoverPathsAsync(account, ct) is { } read
            ? new Turn(
                read.Learnt > 0 ? string.Create(CultureInfo.InvariantCulture, $"the store said where the covers of {read.Learnt} games are, or that it has none") : null,
                read.Error is { } error ? new Trouble(SyncWords.Of(error), read.Detail) : null)
            : null;
}
