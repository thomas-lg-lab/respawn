using Respawn.Games.Ports;

namespace Respawn.Games.Services;

/// <summary>Libraries: which store account is whose, and what a store's answer becomes in the catalogue.
/// It never talks to a store: a connector reads, and hands the reading over.</summary>
public sealed class LibraryService(
    IUnitOfWork unitOfWork,
    ILinkedAccountStore linkedAccounts,
    ISnapshotStore snapshots,
    ICatalogue catalogue,
    ILibraryStore library,
    TimeProvider clock) : ILibraryService
{
    public Task<LinkedAccount> LinkAccountAsync(
        Guid ownerId, string source, string externalId, string? displayName, CancellationToken ct)
    {
        CheckSource(source);
        CheckLength(externalId, Limits.ExternalId, nameof(externalId));
        var name = string.IsNullOrWhiteSpace(displayName) ? null : Shorten(displayName.Trim(), Limits.DisplayName);

        return unitOfWork.RunAsync(
            async () =>
            {
                if (await linkedAccounts.FindAsync(source, externalId, ct) is { } existing)
                {
                    return existing.OwnerId == ownerId
                        ? existing
                        : throw new GamesException(GamesError.LinkedToAnotherAccount);
                }

                var linked = new LinkedAccount(Guid.CreateVersion7(), ownerId, source, externalId, name, clock.GetUtcNow());
                await linkedAccounts.AddAsync(linked, ct);
                return linked;
            },
            ct);
    }

    public Task<Guid> RecordLibraryAsync(Guid linkedAccountId, LibraryReading reading, CancellationToken ct)
    {
        CheckLength(reading.Kind, Limits.SnapshotKind, nameof(reading));
        ArgumentNullException.ThrowIfNull(reading.Payload, nameof(reading));
        var items = Items(reading);

        return unitOfWork.RunAsync(
            async () =>
            {
                var account = await linkedAccounts.FindByIdAsync(linkedAccountId, ct)
                    ?? throw new GamesException(GamesError.UnknownLinkedAccount);
                var now = clock.GetUtcNow();

                // The answer first, as it came: everything below can be derived from it again.
                var snapshot = new RawSnapshot(
                    Guid.CreateVersion7(), account.Source, reading.Kind, now, account.OwnerId, account.Id, reading.Payload);
                await snapshots.AddAsync(snapshot, ct);

                // A game the catalogue does not know gets a canonical record of its own. Finding out
                // that two store entries are the same game is a merge, made later and elsewhere.
                var entries = new Dictionary<string, Guid>(
                    await catalogue.FindStoreEntriesAsync(account.Source, [.. items.Select(item => item.ExternalId)], ct),
                    StringComparer.Ordinal);
                NewStoreEntry[] unknown =
                [
                    .. items
                        .Where(item => !entries.ContainsKey(item.ExternalId))
                        .Select(item => new NewStoreEntry(
                            Guid.CreateVersion7(), Guid.CreateVersion7(), account.Source, item.ExternalId, item.Name)),
                ];
                if (unknown.Length > 0)
                {
                    await catalogue.AddAsync(unknown, now, ct);
                    foreach (var entry in unknown)
                    {
                        entries[entry.ExternalId] = entry.Id;
                    }
                }

                await library.RecordAsync(
                    account.Id,
                    snapshot.Id,
                    [.. items.Select(item => new Ownership(entries[item.ExternalId], item.Playtime, item.LastPlayedAt))],
                    now,
                    ct);
                return snapshot.Id;
            },
            ct);
    }

    public Task<IReadOnlyList<OwnedGame>> GetLibraryAsync(Guid ownerId, CancellationToken ct) => library.ListAsync(ownerId, ct);

    /// <summary>The items of a reading, checked, one per store id: the last one listed wins.</summary>
    private static List<OwnedItem> Items(LibraryReading reading)
    {
        var items = new Dictionary<string, OwnedItem>(StringComparer.Ordinal);
        foreach (var item in reading.Items)
        {
            CheckLength(item.ExternalId, Limits.ExternalId, nameof(reading));
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                throw new ArgumentException($"the game {item.ExternalId} has no name", nameof(reading));
            }

            if (item.Playtime < TimeSpan.Zero)
            {
                throw new ArgumentException($"the game {item.ExternalId} has a negative playtime", nameof(reading));
            }

            items[item.ExternalId] = item with { Name = Shorten(item.Name.Trim(), Limits.Name) };
        }

        return [.. items.Values];
    }

    /// <summary>A source is named in lower-case letters, digits and hyphens, the same everywhere.</summary>
    private static void CheckSource(string source)
    {
        CheckLength(source, Limits.Source, nameof(source));
        if (!source.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
        {
            throw new ArgumentException("a source is named in lower-case letters, digits and hyphens", nameof(source));
        }
    }

    private static void CheckLength(string value, int max, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max)
        {
            throw new ArgumentException($"expected 1 to {max} characters", parameter);
        }
    }

    private static string Shorten(string value, int max) => value.Length > max ? value[..max] : value;
}
