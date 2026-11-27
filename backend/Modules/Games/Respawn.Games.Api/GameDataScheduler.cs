using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Respawn.Games.Ports;

namespace Respawn.Games.Api;

/// <summary>
/// What the game database knows of the catalogue's games, apart from the sync's schedule: every few
/// minutes it asks which stores have games to ask about (<see cref="IGameDataService.GetDueAsync"/>) and
/// has a few of them asked about, store after store. A library read for the first time is spread over
/// several turns; a database that cannot be read is said once and tried again later. Whatever happens
/// here, the sync goes on. An instance with no access to the database asks nothing.
/// </summary>
internal sealed class GameDataScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<GameDataScheduler> logger)
    : DueScheduler<string>(scopes, clock, logger, Period, "game data")
{
    public static readonly TimeSpan Period = TimeSpan.FromMinutes(5);

    protected override Task<IReadOnlyList<string>> DueAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IGameDataService>().GetDueAsync(ct);

    protected override async Task<Turn?> RunAsync(IServiceProvider services, string store, CancellationToken ct) =>
        await services.GetRequiredService<IGameDataService>().ReadAsync(store, ct) is { } read
            ? new Turn(
                read.Matched + read.Unknown > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"the game database knows {read.Matched} of the games asked about, and not {read.Unknown}")
                    : null,
                read.Error is { } error ? new Trouble(SyncWords.Of(error), read.Detail) : null)
            : null;

    protected override string Subject(string key) => $"store {key}";
}
