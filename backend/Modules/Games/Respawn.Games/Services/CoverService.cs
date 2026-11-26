using Respawn.Games.Ports;

namespace Respawn.Games.Services;

/// <summary>The covers: a game's, asked of the sources of its store entries, and where the stores say
/// they are, asked after a sync and kept beside each store entry.</summary>
public sealed class CoverService(
    ICatalogue catalogue,
    ILinkedAccountStore linkedAccounts,
    IEnumerable<ICoverSource> sources,
    TimeProvider clock) : ICoverService
{
    public async Task<Cover> GetCoverAsync(Guid gameId, CancellationToken ct)
    {
        var entries = await catalogue.FindCoverEntriesAsync(gameId, ct)
            ?? throw new GamesException(GamesError.UnknownGame);

        // The same entry first every time, so that a game owned in two stores keeps one cover.
        SourceException? trouble = null;
        foreach (var entry in entries.OrderBy(entry => entry.Source, StringComparer.Ordinal).ThenBy(entry => entry.StoreEntryId))
        {
            if (sources.FirstOrDefault(source => source.Source == entry.Source) is not { } source)
            {
                continue;
            }

            try
            {
                if (await source.ReadCoverAsync(entry.ExternalId, entry.CoverPath, ct) is { } cover)
                {
                    return cover;
                }
            }
            catch (SourceException ex)
            {
                // Another entry may still give one.
                trouble ??= ex;
            }
        }

        // A source that did not answer is not a game without a cover.
        throw trouble is null ? new GamesException(GamesError.NoCover) : trouble;
    }

    public async Task<int> ReadCoverPathsAsync(Guid linkedAccountId, CancellationToken ct)
    {
        if (await linkedAccounts.FindByIdAsync(linkedAccountId, ct) is not { } account
            || sources.FirstOrDefault(source => source.Source == account.Source) is not { } source)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var due = await catalogue.ListCoverPathsDueAsync(account.Id, now - CoverSchedule.AskAgainAfter, ct);
        var asked = 0;
        foreach (var some in due.Chunk(Math.Max(1, source.PathsPerRequest)))
        {
            var said = await source.ReadCoverPathsAsync([.. some.Select(entry => entry.ExternalId)], ct);

            // Kept batch by batch: a store that stops answering half-way does not cost what it said.
            // A game it left out has no cover there for now, and that is kept too.
            await catalogue.SetCoverPathsAsync(
                [
                    .. some.Select(entry => new CoverPath(
                        entry.StoreEntryId,
                        said.TryGetValue(entry.ExternalId, out var path) && path.Length is > 0 and <= Limits.CoverPath ? path : null)),
                ],
                now,
                ct);
            asked += some.Length;
        }

        return asked;
    }
}
