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
        var now = clock.GetUtcNow();
        GameDataEntry[]? asking = null;
        try
        {
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
                asking = some;
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

                // A game the database names but says nothing of is taken as one it does not know: asked
                // about again within days, not left pointing to nothing for a month.
                var said = games.Select(game => game.GameId).ToHashSet(StringComparer.Ordinal);
                GameDataMatch[] matches =
                [
                    .. some.Select(entry => new GameDataMatch(
                        entry.StoreEntryId,
                        chosen.GetValueOrDefault(entry.ExternalId) is { } gameId && said.Contains(gameId) ? gameId : null)),
                ];

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
            var detail = ex.Message;
            var unreadable = gate.Failed(store, clock.GetUtcNow(), ex.Error == SourceError.Unreadable);
            if (unreadable >= GameDataSchedule.UnreadableBeforeSetAside && asking is { Length: > 0 } stuck)
            {
                // The same games, the first due, made the answer unreadable at every turn: set aside as
                // unknown, they let the games after them be asked about, and are asked about again later.
                await work.RunAsync(
                    async () =>
                    {
                        await catalogue.SetGameDataAsync([], [.. stuck.Select(entry => new GameDataMatch(entry.StoreEntryId, null))], now, ct);
                        return true;
                    },
                    ct);
                gate.SetAside(store);
                unknown += stuck.Length;
                detail = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{stuck.Length} games set aside after {unreadable} unreadable answers in a row, asked about again in {GameDataSchedule.UnknownAskAgainAfter.TotalDays} days; {detail}");
            }

            return new GameDataRead(matched, unknown, ex.Error, detail is { Length: > Limits.SyncDetail } ? detail[..Limits.SyncDetail] : detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Not the database's doing (the catalogue could not keep it, say): left alone for as long, so
            // the schedule does not run into it again at every turn. The schedule says what it was.
            gate.Failed(store, clock.GetUtcNow(), unreadable: false);
            throw;
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
