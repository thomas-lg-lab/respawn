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
            // A game it says it has no cover of is kept as such; one it says nothing about is not,
            // or an answer gone empty would pass for a month as games without a cover.
            CoverPath[] learnt =
            [
                .. some
                    .Where(entry => said.ContainsKey(entry.ExternalId))
                    .Select(entry => new CoverPath(
                        entry.StoreEntryId,
                        said[entry.ExternalId] is { Length: > 0 and <= Limits.CoverPath } path ? path : null)),
            ];
            if (learnt.Length > 0)
            {
                await catalogue.SetCoverPathsAsync(learnt, now, ct);
            }

            asked += learnt.Length;
        }

        return asked;
    }
}
