using Respawn.Games.Ports;

namespace Respawn.Games.Services;

// What more than one use case of the module does the same way, written once.

internal static class LinkedAccounts
{
    /// <summary>The owner's linked account. One that is not there and one that is someone else's are
    /// not told apart: nobody learns which accounts exist.</summary>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownLinkedAccount"/>.</exception>
    public static async Task<LinkedAccount> OwnedAsync(
        this ILinkedAccountStore linkedAccounts, Guid ownerId, Guid linkedAccountId, CancellationToken ct) =>
        await linkedAccounts.FindByIdAsync(linkedAccountId, ct) is { } account && account.OwnerId == ownerId
            ? account
            : throw new GamesException(GamesError.UnknownLinkedAccount);
}

internal static class Details
{
    /// <summary>What happened, in a connector's words, cut to what is kept of it
    /// (<see cref="Limits.SyncDetail"/>).</summary>
    public static string? Cut(string? detail) =>
        detail is { Length: > Limits.SyncDetail } ? detail[..Limits.SyncDetail] : detail;
}

internal static class OwnedGames
{
    /// <summary>A library from the entries it is made of, a game once however many entries it is owned
    /// through. What a store said was played lately is no longer recent once its window has gone by
    /// since that reading; a store no connector reads cannot say its window, so its figure is not kept
    /// either.</summary>
    public static List<OwnedGame> Of(IReadOnlyList<OwnedEntry> entries, IEnumerable<ILibrarySource> sources, DateTimeOffset now)
    {
        var windows = sources.ToDictionary(source => source.Source, source => source.RecentWindow, StringComparer.Ordinal);
        return
        [
            .. entries
                .Select(entry => entry.RecentPlaytime is not null
                    && !(windows.TryGetValue(entry.Source, out var window) && entry.AsOf + window > now)
                        ? entry with { RecentPlaytime = null }
                        : entry)
                .GroupBy(entry => entry.GameId)
                .Select(Game),
        ];
    }

    /// <summary>Whether a store says the game was played: some playtime, or a last session.</summary>
    public static bool Launched(OwnedGame game) => game.Playtime > TimeSpan.Zero || game.LastPlayedAt is not null;

    /// <summary>The times that were said, added up; null while none was.</summary>
    public static TimeSpan? Sum(IEnumerable<TimeSpan?> times)
    {
        TimeSpan? total = null;
        foreach (var time in times)
        {
            if (time is { } said)
            {
                total = (total ?? TimeSpan.Zero) + said;
            }
        }

        return total;
    }

    /// <summary>A game from the entries it is owned through: its totals add up, its last session is
    /// the latest, and its entries come in an order that does not change.</summary>
    private static OwnedGame Game(IGrouping<Guid, OwnedEntry> owned)
    {
        OwnedEntry[] entries =
        [
            .. owned.OrderBy(entry => entry.Source, StringComparer.Ordinal)
                .ThenBy(entry => entry.ExternalId, StringComparer.Ordinal)
                .ThenBy(entry => entry.LinkedAccountId),
        ];
        return new OwnedGame(
            owned.Key,
            entries[0].GameName,
            Sum(entries.Select(entry => entry.Playtime)),
            Sum(entries.Select(entry => entry.RecentPlaytime)),
            entries.Max(entry => entry.LastPlayedAt),
            entries,
            entries[0].CoverSource);
    }
}

internal static class RemovedGames
{
    /// <summary>The games taken out of a library from the entries taken out, a game once, its entries
    /// in an order that does not change.</summary>
    public static List<RemovedGame> Of(IReadOnlyList<RemovedEntry> entries) =>
    [
        .. entries.GroupBy(entry => entry.GameId).Select(removed =>
        {
            RemovedEntry[] ordered =
            [
                .. removed.OrderBy(entry => entry.Source, StringComparer.Ordinal)
                    .ThenBy(entry => entry.ExternalId, StringComparer.Ordinal)
                    .ThenBy(entry => entry.LinkedAccountId),
            ];
            return new RemovedGame(
                removed.Key,
                ordered[0].GameName,
                ordered.Where(entry => entry.NoticeDismissedAt is null).Min(entry => entry.ListedAgainSince),
                ordered);
        }),
    ];
}

internal static class StoreEntries
{
    /// <summary>The store entries of these games: those the catalogue has, as it has them, and the id
    /// of each by its id in the store, one added for every game the catalogue did not know. Such a game
    /// gets a canonical record of its own, under the name given (none, for an answer that names no
    /// game): finding out that two store entries are the same game is a merge, made later and
    /// elsewhere.</summary>
    public static async Task<(IReadOnlyDictionary<string, StoreEntry> Known, IReadOnlyDictionary<string, Guid> Ids)> EnsureAsync(
        this ICatalogue catalogue,
        string source,
        IReadOnlyCollection<(string ExternalId, string? Name)> games,
        DateTimeOffset at,
        CancellationToken ct)
    {
        var known = await catalogue.FindStoreEntriesAsync(source, [.. games.Select(game => game.ExternalId)], ct);
        NewStoreEntry[] unknown =
        [
            .. games
                .Where(game => !known.ContainsKey(game.ExternalId))
                .Select(game => new NewStoreEntry(Guid.CreateVersion7(), Guid.CreateVersion7(), source, game.ExternalId, game.Name)),
        ];
        if (unknown.Length > 0)
        {
            await catalogue.AddAsync(unknown, at, ct);
        }

        var ids = known.ToDictionary(entry => entry.Key, entry => entry.Value.Id, StringComparer.Ordinal);
        foreach (var entry in unknown)
        {
            ids[entry.ExternalId] = entry.Id;
        }

        return (known, ids);
    }
}
