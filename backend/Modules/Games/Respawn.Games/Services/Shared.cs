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
