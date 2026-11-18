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
        if (reading.Payload is null || reading.Items is null)
        {
            throw new ArgumentException("a reading has a payload and a list of games, empty when it lists none", nameof(reading));
        }

        var takenAt = reading.TakenAt.ToUniversalTime();
        if (takenAt > clock.GetUtcNow() + ClockSkew)
        {
            throw new ArgumentException("the reading is dated in the future", nameof(reading));
        }

        var items = Items(reading);

        // An empty answer is what a profile made private looks like: never the proof of an empty library.
        var complete = reading.Complete && items.Count > 0;

        return unitOfWork.RunAsync(
            async () =>
            {
                var account = await linkedAccounts.FindByIdAsync(linkedAccountId, ct)
                    ?? throw new GamesException(GamesError.UnknownLinkedAccount);
                var now = clock.GetUtcNow();

                // The answer first, as it came: everything below can be derived from it again.
                var snapshot = new RawSnapshot(
                    Guid.CreateVersion7(), account.Source, reading.Kind, takenAt, now, account.OwnerId, account.Id, reading.Payload, complete);
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
                var said = new List<Observation>();
                var renames = new List<StoreEntryRename>();
                foreach (var item in items)
                {
                    var entry = entries[item.ExternalId];
                    var current = before.GetValueOrDefault(entry);
                    if (current is not null && current.AsOf > takenAt)
                    {
                        // Older than what is known: it is history, taken at its word, and may only
                        // tell the game was owned earlier than thought.
                        said.Add(new Observation(entry, item.Playtime, item.LastPlayedAt, item.Playtime, item.RecentPlaytime));
                        if (takenAt < current.FirstSeenAt)
                        {
                            owned.Add(current with { FirstSeenAt = takenAt });
                        }

                        continue;
                    }

                    var updated = Updated(current, item, entry, takenAt, snapshot.Id);
                    owned.Add(updated);
                    said.Add(new Observation(entry, item.Playtime, item.LastPlayedAt, updated.Playtime, item.RecentPlaytime));
                    if (item.Name is not null && known.TryGetValue(item.ExternalId, out var kept) && kept.Name != item.Name)
                    {
                        renames.Add(new StoreEntryRename(kept.Id, item.Name));
                    }
                }

                if (renames.Count > 0)
                {
                    await catalogue.RenameAsync(renames, ct);
                }

                if (complete)
                {
                    // The whole library, and these are not in it any more: gone since this reading,
                    // unless something more recent already says otherwise.
                    var listed = items.Select(item => entries[item.ExternalId]).ToHashSet();
                    owned.AddRange(before.Values
                        .Where(gone => !listed.Contains(gone.StoreEntryId) && gone.UnlistedSince is null && gone.AsOf <= takenAt)
                        .Select(gone => gone with { UnlistedSince = takenAt, RecentPlaytime = null }));
                }

                await library.RecordAsync(account.Id, snapshot.Id, said, owned, ct);
                return snapshot.Id;
            },
            ct);
    }

    public async Task<LibraryPage> GetLibraryAsync(Guid ownerId, LibrarySort sort, int offset, int limit, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, Limits.LibraryPage);

        // The whole library is read and put in order here: a few thousand rows at most, and the order
        // is then the same on every database, whatever its collation.
        var games = (await library.ListAsync(ownerId, ct))
            .GroupBy(entry => entry.GameId)
            .Select(Game)
            .ToList();
        games.Sort(Order(sort));
        return new LibraryPage(games.Count, [.. games.Skip(offset).Take(limit)]);
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
            entries);
    }

    private static TimeSpan? Sum(IEnumerable<TimeSpan?> times)
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

    /// <summary>The order of a library. What cannot be placed comes last, and two games the order does
    /// not tell apart come by name, then by id: a page never depends on chance.</summary>
    private static Comparison<OwnedGame> Order(LibrarySort sort)
    {
        // Letters before their case and their accents, the same in every language of the interface.
        var names = StringComparer.InvariantCultureIgnoreCase;
        int ByName(OwnedGame a, OwnedGame b)
        {
            var order = (a.Name, b.Name) switch
            {
                (null, null) => 0,
                (null, _) => 1,
                (_, null) => -1,
                _ => names.Compare(a.Name, b.Name),
            };
            return order != 0 ? order : a.GameId.CompareTo(b.GameId);
        }

        int Then(int order, OwnedGame a, OwnedGame b) => order != 0 ? order : ByName(a, b);

        return sort switch
        {
            LibrarySort.Playtime => (a, b) => Then(Descending(a.Playtime, b.Playtime), a, b),
            LibrarySort.LastPlayed => (a, b) => Then(Descending(a.LastPlayedAt, b.LastPlayedAt), a, b),
            _ => ByName,
        };
    }

    /// <summary>The greatest first, and what is not known last.</summary>
    private static int Descending<T>(T? a, T? b)
        where T : struct, IComparable<T> =>
        (a, b) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => b.Value.CompareTo(a.Value),
        };

    /// <summary>What is known of a game after a reading at least as recent as the last one. An answer
    /// may be poorer than the one before (a profile made private, a store having a bad day): what it
    /// does not say is kept, and a playtime of zero is taken as not said when one is known. A playtime
    /// that goes down without reaching zero is the store correcting itself, and is taken. Listing a
    /// game brings it back into the library, unless a complete reading more recent than this one has
    /// already left it out: a reading that arrives late does not undo what is known since.</summary>
    private static Ownership Updated(Ownership? current, OwnedItem item, Guid entry, DateTimeOffset takenAt, Guid snapshot)
    {
        var silent = item.Playtime is null || (item.Playtime == TimeSpan.Zero && current?.Playtime > TimeSpan.Zero);
        var firstSeen = current is not null && current.FirstSeenAt < takenAt ? current.FirstSeenAt : takenAt;
        var goneSince = current?.UnlistedSince > takenAt ? current.UnlistedSince : null;
        return new Ownership(
            entry,
            silent ? current?.Playtime : item.Playtime,
            item.LastPlayedAt ?? current?.LastPlayedAt,
            firstSeen,
            takenAt,
            snapshot,
            goneSince is null ? item.RecentPlaytime : null,
            goneSince);
    }

    /// <summary>The items of a reading, checked, one per store id: the last one listed wins.</summary>
    private static List<OwnedItem> Items(LibraryReading reading)
    {
        var items = new Dictionary<string, OwnedItem>(StringComparer.Ordinal);
        foreach (var item in reading.Items)
        {
            CheckLength(item.ExternalId, Limits.ExternalId, nameof(reading));
            if (item.Playtime < TimeSpan.Zero || item.RecentPlaytime < TimeSpan.Zero)
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

    /// <summary>A name as it is kept: without the control characters a source may let through (the
    /// database refuses some of them, and none is part of a name), trimmed, cut to the size of its
    /// column, and null when blank.</summary>
    private static string? Clean(string? name, int max)
    {
        if (name is not null && name.Any(char.IsControl))
        {
            name = string.Concat(name.Where(c => !char.IsControl(c)));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
