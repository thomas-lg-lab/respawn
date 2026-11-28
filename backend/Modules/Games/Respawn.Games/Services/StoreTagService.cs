using Respawn.Games.Ports;

namespace Respawn.Games.Services;

/// <summary>The tags the stores' players gave the catalogue's games, asked a few games at a time on a
/// schedule of their own, with the names of the tags.</summary>
public sealed class StoreTagService(
    ICatalogue catalogue,
    ISnapshotStore snapshots,
    IUnitOfWork work,
    IEnumerable<IStoreTagSource> sources,
    StoreTagGate gate,
    TimeProvider clock) : IStoreTagService
{
    public async Task<IReadOnlyList<string>> GetDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        string[] stores =
        [
            .. sources.Select(source => source.Source)
                .Where(store => !(gate.FailedAt(store) > now - StoreTagSchedule.RetryAfter))
                .Distinct(StringComparer.Ordinal),
        ];
        return stores.Length == 0
            ? []
            : [.. (await catalogue.ListStoresWithStoreTagsDueAsync(stores, StoreTagSchedule.DueBefore(now), ct)).Order(StringComparer.Ordinal)];
    }

    public async Task<StoreTagsRead?> ReadAsync(string store, CancellationToken ct)
    {
        if (sources.FirstOrDefault(source => source.Source == store) is not { } source || !gate.TryEnter(store))
        {
            return null;
        }

        var read = 0;
        try
        {
            var now = clock.GetUtcNow();
            if (gate.FailedAt(store) > now - StoreTagSchedule.RetryAfter)
            {
                return null;
            }

            var due = await catalogue.ListStoreTagsDueAsync(store, StoreTagSchedule.DueBefore(now), StoreTagSchedule.EntriesPerTurn, ct);
            if (due.Count == 0)
            {
                return null;
            }

            // The names first, once a turn: a tag the store gave since the last turn has one before any
            // game shows it.
            var names = await source.ReadTagNamesAsync(ct);
            if (names.Names.Count == 0)
            {
                throw new SourceException(SourceError.Unreadable, "the store's answer names no tag");
            }

            var namesKept = false;
            foreach (var some in due.Chunk(Math.Max(1, source.GamesPerRequest)))
            {
                // An answer the connector could not read throws; one it read speaks for the games it names.
                var reading = await source.ReadTagsAsync([.. some.Select(entry => entry.ExternalId)], ct);

                // Kept batch by batch: a store that stops answering half-way does not cost what it said.
                // A game the answer leaves out keeps the tags it had, and goes to the back of the queue:
                // a store that never names it does not hold back the games after it.
                SourceAnswer[] answers = [.. namesKept ? [] : names.Answers, .. reading.Answers];
                await work.RunAsync(
                    async () =>
                    {
                        foreach (var answer in answers)
                        {
                            await snapshots.AddAsync(
                                new RawSnapshot(Guid.CreateVersion7(), source.Source, answer.Kind, answer.TakenAt, clock.GetUtcNow(), null, answer.Payload),
                                ct);
                        }

                        await catalogue.SetStoreTagsAsync(
                            store,
                            namesKept ? [] : names.Names,
                            [.. some.Select(entry => new StoreEntryTags(entry.StoreEntryId, reading.Tags.GetValueOrDefault(entry.ExternalId)))],
                            now,
                            ct);
                        return true;
                    },
                    ct);
                namesKept = true;
                read += some.Length;
            }

            gate.Succeeded(store);
            return new StoreTagsRead(read);
        }
        catch (SourceException ex)
        {
            gate.Failed(store, clock.GetUtcNow());
            return new StoreTagsRead(read, ex.Error, Details.Cut(ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Not the store's doing (the catalogue could not keep it, say): left alone for as long, so the
            // schedule does not run into it again at every turn. The schedule says what it was.
            gate.Failed(store, clock.GetUtcNow());
            throw;
        }
        finally
        {
            gate.Leave(store);
        }
    }

    public Task<IReadOnlyList<NamedStoreTag>> GetNamesAsync(CancellationToken ct) => catalogue.ListStoreTagNamesAsync(ct);
}
