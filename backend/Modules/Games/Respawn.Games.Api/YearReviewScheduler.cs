using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Respawn.Games.Ports;

namespace Respawn.Games.Api;

/// <summary>
/// The import of the stores' yearly reviews, apart from the sync's schedule: every little while it asks
/// which linked accounts miss a review (<see cref="IYearReviewService.GetDueAsync"/>) and has them
/// imported one after the other. A newly linked account gets its past once its first sync ended; the
/// year just over comes in once the store has made its review. Whatever happens here, the sync goes on.
/// </summary>
internal sealed class YearReviewScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<YearReviewScheduler> logger)
    : DueScheduler(scopes, clock, logger, Period, "year in review")
{
    public static readonly TimeSpan Period = TimeSpan.FromMinutes(15);

    protected override Task<IReadOnlyList<Guid>> DueAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IYearReviewService>().GetDueAsync(ct);

    protected override async Task<Turn?> RunAsync(IServiceProvider services, Guid account, CancellationToken ct) =>
        await services.GetRequiredService<IYearReviewService>().ImportAsync(account, ct) is { } import
            ? new Turn(
                import.Imported.Count > 0 ? $"{string.Join(", ", import.Imported)} imported" : null,
                import.Error is { } error ? new Trouble(SyncWords.Of(error), import.Detail) : null)
            : null;
}
