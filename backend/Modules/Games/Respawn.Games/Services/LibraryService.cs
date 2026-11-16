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
    /// <summary>How far ahead of this clock a source's may be. A reading dated later than that is a
    /// mistake, and would make every later one look old.</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    public Task<LinkedAccount> LinkAccountAsync(
        Guid ownerId, string source, string externalId, string? displayName, CancellationToken ct)
    {
        CheckSource(source);
        CheckLength(externalId, Limits.ExternalId, nameof(externalId));
        var name = Clean(displayName, Limits.DisplayName);

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
        var takenAt = reading.TakenAt.ToUniversalTime();
        if (takenAt > clock.GetUtcNow() + ClockSkew)
        {
            throw new ArgumentException("the reading is dated in the future", nameof(reading));
        }

        var items = Items(reading);

        return unitOfWork.RunAsync(
            async () =>
            {
                var account = await linkedAccounts.FindByIdAsync(linkedAccountId, ct)
                    ?? throw new GamesException(GamesError.UnknownLinkedAccount);
                var now = clock.GetUtcNow();

                // The answer first, as it came: everything below can be derived from it again.
                var snapshot = new RawSnapshot(
                    Guid.CreateVersion7(), account.Source, reading.Kind, takenAt, now, account.OwnerId, account.Id, reading.Payload);
                await snapshots.AddAsync(snapshot, ct);

                // A game the catalogue does not know gets a canonical record of its own. Finding out
                // that two store entries are the same game is a merge, made later and elsewhere.
                var known = await catalogue.FindStoreEntriesAsync(account.Source, [.. items.Select(item => item.ExternalId)], ct);
                NewStoreEntry[] unknown =
                [
                    .. items
                        .Where(item => !known.ContainsKey(item.ExternalId))
                        .Select(item => new NewStoreEntry(
                            Guid.CreateVersion7(), Guid.CreateVersion7(), account.Source, item.ExternalId, item.Name)),
                ];
                if (unknown.Length > 0)
                {
                    await catalogue.AddAsync(unknown, now, ct);
                }

                var entries = known.ToDictionary(entry => entry.Key, entry => entry.Value.Id, StringComparer.Ordinal);
                foreach (var entry in unknown)
                {
                    entries[entry.ExternalId] = entry.Id;
                }

                // What the reading says is kept whole; what is known only moves by the rules.
                var before = await library.FindOwnershipsAsync(account.Id, ct);
                var owned = new List<Ownership>();
                var renames = new List<StoreEntryRename>();
                foreach (var item in items)
                {
                    var entry = entries[item.ExternalId];
                    var current = before.GetValueOrDefault(entry);
                    if (current is not null && current.AsOf > takenAt)
                    {
                        // Older than what is known: it is history, and may only tell the game was
                        // owned earlier than thought.
                        if (takenAt < current.FirstSeenAt)
                        {
                            owned.Add(current with { FirstSeenAt = takenAt });
                        }

                        continue;
                    }

                    owned.Add(Updated(current, item, entry, takenAt, snapshot.Id));
                    if (item.Name is not null && known.TryGetValue(item.ExternalId, out var kept) && kept.Name != item.Name)
                    {
                        renames.Add(new StoreEntryRename(kept.Id, kept.Name, item.Name));
                    }
                }

                if (renames.Count > 0)
                {
                    await catalogue.RenameAsync(renames, ct);
                }

                await library.RecordAsync(
                    account.Id,
                    snapshot.Id,
                    [.. items.Select(item => new Observation(entries[item.ExternalId], item.Playtime, item.LastPlayedAt))],
                    owned,
                    ct);
                return snapshot.Id;
            },
            ct);
    }

    public Task<IReadOnlyList<OwnedGame>> GetLibraryAsync(Guid ownerId, CancellationToken ct) => library.ListAsync(ownerId, ct);

    /// <summary>What is known of a game after a reading at least as recent as the last one. An answer
    /// may be poorer than the one before (a profile made private, a store having a bad day): what it
    /// does not say is kept, and a playtime of zero is taken as not said when one is known. A playtime
    /// that goes down without reaching zero is the store correcting itself, and is taken.</summary>
    private static Ownership Updated(Ownership? current, OwnedItem item, Guid entry, DateTimeOffset takenAt, Guid snapshot)
    {
        var silent = item.Playtime is null || (item.Playtime == TimeSpan.Zero && current?.Playtime > TimeSpan.Zero);
        var firstSeen = current is not null && current.FirstSeenAt < takenAt ? current.FirstSeenAt : takenAt;
        return new Ownership(
            entry,
            silent ? current?.Playtime : item.Playtime,
            item.LastPlayedAt ?? current?.LastPlayedAt,
            firstSeen,
            takenAt,
            snapshot);
    }

    /// <summary>The items of a reading, checked, one per store id: the last one listed wins.</summary>
    private static List<OwnedItem> Items(LibraryReading reading)
    {
        var items = new Dictionary<string, OwnedItem>(StringComparer.Ordinal);
        foreach (var item in reading.Items)
        {
            CheckLength(item.ExternalId, Limits.ExternalId, nameof(reading));
            if (item.Playtime < TimeSpan.Zero)
            {
                throw new ArgumentException($"the game {item.ExternalId} has a negative playtime", nameof(reading));
            }

            items[item.ExternalId] = item with
            {
                Name = Clean(item.Name, Limits.Name),
                LastPlayedAt = item.LastPlayedAt?.ToUniversalTime(),
            };
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

    /// <summary>A name as it is kept: trimmed, cut to the size of its column, and null when blank.</summary>
    private static string? Clean(string? name, int max)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
