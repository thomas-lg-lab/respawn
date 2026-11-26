using Respawn.Games.Ports;

namespace Respawn.Games.Services;

/// <summary>The covers: a game's, asked of the sources of its store entries, and where the stores say
/// they are, asked on a schedule of its own and kept beside each store entry.</summary>
public sealed class CoverService(
    ICatalogue catalogue,
    ILinkedAccountStore linkedAccounts,
    IEnumerable<ICoverSource> sources,
    CoverGate gate,
    TimeProvider clock) : ICoverService
{
    public async Task<CoverAnswer> GetCoverAsync(Guid gameId, CancellationToken ct)
    {
        var entries = await catalogue.FindCoverEntriesAsync(gameId, ct)
            ?? throw new GamesException(GamesError.UnknownGame);

        // The same entry first every time, so that a game owned in two stores keeps one cover.
        SourceException? trouble = null;
        var settled = true;
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
                    return new CoverAnswer(cover, entry.Asked);
                }

                settled &= entry.Asked;
            }
            catch (SourceException ex)
            {
                // Another entry may still give one.
                trouble ??= ex;
            }
        }

        // A source that did not answer is not a game without a cover.
        return trouble is null ? new CoverAnswer(null, settled) : throw trouble;
    }

    public async Task<IReadOnlyList<Guid>> GetDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = (await catalogue.ListAccountsWithCoverPathsDueAsync(now - CoverSchedule.AskAgainAfter, ct)).ToHashSet();
        if (due.Count == 0)
        {
            return [];
        }

        var readable = sources.Select(source => source.Source).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. (await linkedAccounts.ListAllAsync(ct))
                .Where(account => due.Contains(account.Id)
                    && readable.Contains(account.Source)
                    && !(gate.TriedAt(account.Id) > now - CoverSchedule.RetryAfter))
                .OrderBy(account => account.LinkedAt)
                .ThenBy(account => account.Id)
                .Select(account => account.Id),
        ];
    }

    public async Task<CoverPathsRead?> ReadCoverPathsAsync(Guid linkedAccountId, CancellationToken ct)
    {
        if (await linkedAccounts.FindByIdAsync(linkedAccountId, ct) is not { } account
            || sources.FirstOrDefault(source => source.Source == account.Source) is not { } source
            || !gate.TryEnter(account.Id))
        {
            return null;
        }

        var learnt = 0;
        try
        {
            var now = clock.GetUtcNow();
            if (gate.TriedAt(account.Id) > now - CoverSchedule.RetryAfter)
            {
                return null;
            }

            // Whatever comes of it: a store that fails, or leaves games out, is not asked again at
            // every turn of the schedule.
            gate.Tried(account.Id, now);
            var due = await catalogue.ListCoverPathsDueAsync(account.Id, now - CoverSchedule.AskAgainAfter, ct);
            foreach (var some in due.Chunk(Math.Max(1, source.PathsPerRequest)))
            {
                var said = await source.ReadCoverPathsAsync([.. some.Select(entry => entry.ExternalId)], ct);
                if (said.Count == 0)
                {
                    // An answer gone empty is not a batch of games without a cover: nothing is kept
                    // of it, and it is said.
                    throw new SourceException(SourceError.Unreadable, "the store's answer names none of the games it was asked about");
                }

                // Kept batch by batch: a store that stops answering half-way does not cost what it
                // said. A game it says it has no cover of is kept as such; so is one that an answer
                // naming others leaves out, which the store then does not know.
                await catalogue.SetCoverPathsAsync(
                    [
                        .. some.Select(entry => new CoverPath(
                            entry.StoreEntryId,
                            said.GetValueOrDefault(entry.ExternalId) is { Length: > 0 and <= Limits.CoverPath } path ? path : null)),
                    ],
                    now,
                    ct);
                learnt += some.Length;
            }

            return new CoverPathsRead(learnt);
        }
        catch (SourceException ex)
        {
            return new CoverPathsRead(learnt, ex.Error, ex.Message is { Length: > Limits.SyncDetail } detail ? detail[..Limits.SyncDetail] : ex.Message);
        }
        finally
        {
            gate.Leave(account.Id);
        }
    }
}
