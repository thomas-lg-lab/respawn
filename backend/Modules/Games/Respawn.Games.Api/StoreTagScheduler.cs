using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Respawn.Games.Ports;

namespace Respawn.Games.Api;

/// <summary>
/// The tags the stores' players gave the catalogue's games, apart from the sync's schedule: every few
/// minutes it asks which stores have games to ask about (<see cref="IStoreTagService.GetDueAsync"/>) and
/// has a few of them asked about, store after store. A library read for the first time is spread over
/// several turns; a store that cannot be read is said once and tried again later. Whatever happens here,
/// the sync goes on.
/// </summary>
internal sealed class StoreTagScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<StoreTagScheduler> logger)
    : DueScheduler<string>(scopes, clock, logger, Period, "store tags")
{
    public static readonly TimeSpan Period = TimeSpan.FromMinutes(5);

    protected override Task<IReadOnlyList<string>> DueAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IStoreTagService>().GetDueAsync(ct);

    protected override async Task<Turn?> RunAsync(IServiceProvider services, string store, CancellationToken ct) =>
        await services.GetRequiredService<IStoreTagService>().ReadAsync(store, ct) is { } read
            ? new Turn(
                read.Read > 0 ? string.Create(CultureInfo.InvariantCulture, $"the store gave the tags of {read.Read} games") : null,
                read.Error is { } error ? new Trouble(SyncWords.Of(error), read.Detail) : null)
            : null;

    protected override string Subject(string key) => $"store {key}";
}
