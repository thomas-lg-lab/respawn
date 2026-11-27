using System.Globalization;
using Respawn.Games.Ports;

namespace Respawn.Games.Services;

/// <summary>What the game database knows of the catalogue's games: which of its games each store entry
/// is, and what it says of those, asked a few games at a time on a schedule of its own.</summary>
public sealed class GameDataService(
    ICatalogue catalogue,
    ISnapshotStore snapshots,
    IUnitOfWork work,
    IGameDataSource source,
    GameDataGate gate,
    GameDataOptions options,
    TimeProvider clock) : IGameDataService
{
    public async Task<IReadOnlyList<string>> GetDueAsync(CancellationToken ct)
    {
        if (!source.HasAccess)
        {
            return [];
        }

        var now = clock.GetUtcNow();
        var stores = source.Stores.Where(store => !(gate.FailedAt(store) > now - GameDataSchedule.RetryAfter)).ToArray();
        return stores.Length == 0
            ? []
            : [.. (await catalogue.ListStoresWithGameDataDueAsync(stores, GameDataSchedule.DueAt(now), ct)).Order(StringComparer.Ordinal)];
    }

    public async Task<GameDataRead?> ReadAsync(string store, CancellationToken ct)
    {
        if (!source.Stores.Contains(store) || !source.HasAccess || !gate.TryEnter(store))
        {
            return null;
        }

        var (matched, unknown) = (0, 0);
        try
        {
            var now = clock.GetUtcNow();
            if (gate.FailedAt(store) > now - GameDataSchedule.RetryAfter)
            {
                return null;
            }

            var due = await catalogue.ListGameDataDueAsync(store, GameDataSchedule.DueAt(now), Math.Max(1, options.GamesPerTurn), ct);
            if (due.Count == 0)
            {
                return null;
            }

            foreach (var some in due.Chunk(Math.Max(1, source.MatchesPerRequest)))
            {
                var reading = await source.MatchAsync(store, [.. some.Select(entry => entry.ExternalId)], ct);

                // One of the database's games per store entry: the smallest id, the same every time.
                var chosen = reading.Matches
                    .GroupBy(match => match.ExternalId, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(match => match.GameId).OrderBy(Number).ThenBy(id => id, StringComparer.Ordinal).First(),
                        StringComparer.Ordinal);

                var answers = new List<SourceAnswer>(reading.Payloads.Select(payload => new SourceAnswer(reading.Kind, reading.TakenAt, payload)));
                var games = new List<GameFacts>();
                foreach (var ids in chosen.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Chunk(Math.Max(1, source.GamesPerRequest)))
                {
                    var read = await source.ReadGamesAsync(ids, ct);
                    answers.AddRange(read.Answers);
                    games.AddRange(read.Games);
                }

                GameDataMatch[] matches = [.. some.Select(entry => new GameDataMatch(entry.StoreEntryId, chosen.GetValueOrDefault(entry.ExternalId)))];

                // Kept batch by batch: a database that stops answering half-way does not cost what it said.
                await work.RunAsync(
                    async () =>
                    {
                        foreach (var answer in answers)
                        {
                            await snapshots.AddAsync(
                                new RawSnapshot(Guid.CreateVersion7(), source.Source, answer.Kind, answer.TakenAt, clock.GetUtcNow(), null, answer.Payload),
                                ct);
                        }

                        await catalogue.SetGameDataAsync(games, matches, now, ct);
                        return true;
                    },
                    ct);
                matched += matches.Count(match => match.GameId is not null);
                unknown += matches.Count(match => match.GameId is null);
            }

            gate.Succeeded(store);
            return new GameDataRead(matched, unknown);
        }
        catch (SourceException ex)
        {
            gate.Failed(store, clock.GetUtcNow());
            return new GameDataRead(matched, unknown, ex.Error, ex.Message is { Length: > Limits.SyncDetail } detail ? detail[..Limits.SyncDetail] : ex.Message);
        }
        finally
        {
            gate.Leave(store);
        }
    }

    /// <summary>An id as a number, for the smallest to come first; one that is not sorts last.</summary>
    private static long Number(string id) =>
        long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : long.MaxValue;
}
