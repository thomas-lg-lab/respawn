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
    IEnumerable<ILibrarySource> sources,
    IGameDataSource gameData,
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

    public async Task<IReadOnlyList<LinkedAccount>> GetLinkedAccountsAsync(Guid ownerId, CancellationToken ct) =>
        [.. (await linkedAccounts.ListAsync(ownerId, ct)).OrderBy(account => account.LinkedAt).ThenBy(account => account.Id)];

    public Task UnlinkAccountAsync(Guid ownerId, Guid linkedAccountId, CancellationToken ct) =>
        unitOfWork.RunAsync(
            async () =>
            {
                // Under the lock a reading takes: a reading on its way ends first, or finds no account.
                var account = await linkedAccounts.OwnedAsync(ownerId, linkedAccountId, ct);
                await linkedAccounts.RemoveAsync(account.Id, ct);
                return true;
            },
            ct);

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
                    Guid.CreateVersion7(), account.Source, reading.Kind, takenAt, now, account.Id, reading.Payload, complete);
                await snapshots.AddAsync(snapshot, ct);

                // A game the catalogue does not know gets a canonical record of its own.
                var (known, entries) = await catalogue.EnsureAsync(
                    account.Source, [.. items.Select(item => (item.ExternalId, item.Name))], now, ct);

                // What the reading says is kept whole; what is known only moves by the rules.
                var before = await library.FindOwnershipsAsync(account.Id, ct);
                var owned = new List<Ownership>();
                var said = new List<Observation>();

                // A store that hides the playtime answers zero for every game. A reading with no time at
                // all is taken as that: where nothing was known, its zeros are not a total to count from,
                // or the day the store says again would be given everything ever played.
                var saysNoTime = !items.Any(item => item.Playtime > TimeSpan.Zero);
                var renames = new List<StoreEntryRename>();
                foreach (var item in items)
                {
                    var entry = entries[item.ExternalId];
                    var current = before.GetValueOrDefault(entry);
                    if (current is not null && current.AsOf > takenAt)
                    {
                        // Older than what is known: it is history. Its total is taken at its word,
                        // but for a zero that says nothing (Known), and of what is known it may only
                        // tell the game was owned earlier than thought.
                        said.Add(new Observation(entry, item.Playtime, item.LastPlayedAt, Known(item.Playtime, current.Playtime, saysNoTime), item.RecentPlaytime));
                        if (takenAt < current.FirstSeenAt)
                        {
                            owned.Add(current with { FirstSeenAt = takenAt });
                        }

                        continue;
                    }

                    var updated = Updated(current, item, entry, takenAt, snapshot.Id);
                    owned.Add(updated);
                    said.Add(new Observation(entry, item.Playtime, item.LastPlayedAt, Known(updated.Playtime, current?.Playtime, saysNoTime), item.RecentPlaytime));
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
                    // The whole library, and these are not in it: noted, since the earliest reading
                    // that says so, unless something more recent lists them. Nothing is concluded:
                    // the answer may be cut short and still agree with itself, so the games stay.
                    // A game its owner took out is noted the same way, and is no longer back.
                    var listed = items.Select(item => entries[item.ExternalId]).ToHashSet();
                    owned.AddRange(before.Values
                        .Where(absent => !listed.Contains(absent.StoreEntryId)
                            && absent.AsOf <= takenAt
                            && (absent.MissingSince is null || absent.MissingSince > takenAt))
                        .Select(absent => absent with { MissingSince = takenAt, RecentPlaytime = null, ListedAgainSince = null }));
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
        var games = await GamesAsync(ownerId, ct);
        games.Sort(Order(sort));
        return new LibraryPage(games.Count, [.. games.Skip(offset).Take(limit)]);
    }

    public async Task<LibraryStats> GetStatsAsync(Guid ownerId, CancellationToken ct)
    {
        var games = await GamesAsync(ownerId, ct);
        var entries = games.SelectMany(game => game.Entries).ToList();
        var launched = games.Count(OwnedGames.Launched);
        return new LibraryStats(
            games.Count,
            launched,
            games.Count - launched,
            games.Count(game => game.Entries.All(entry => entry.MissingSince is not null)),
            OwnedGames.Sum(games.Select(game => game.Playtime)),
            OwnedGames.Sum(games.Select(game => game.RecentPlaytime)),
            games.Count(game => game.RecentPlaytime > TimeSpan.Zero),
            entries.Count > 0 ? entries.Min(entry => entry.FirstSeenAt) : null);
    }

    public async Task<LibraryHistory> GetHistoryAsync(Guid ownerId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var played = await library.ListPlayedAsync(ownerId, gameId: null, from, to, ct);
        return new LibraryHistory(
            await library.FindFirstSeenAsync(ownerId, ct),
            [
                .. played
                    .GroupBy(day => day.Date)
                    .OrderBy(day => day.Key)
                    .Select(day => new HistoryDay(
                        day.Key,
                        [
                            // An order that does not change from one answer to the next.
                            .. day.OrderByDescending(game => game.Playtime)
                                .ThenBy(game => game.GameId)
                                .Select(game => new PlayedGame(game.GameId, game.Playtime)),
                        ])),
            ]);
    }

    public Task RemoveGameAsync(Guid ownerId, Guid gameId, CancellationToken ct) =>
        unitOfWork.RunAsync(
            async () =>
            {
                // Under the lock a reading takes: what the store last said is settled while this decides.
                var inLibrary = (await library.FindGameOwnershipsAsync(ownerId, gameId, ct))
                    .Where(owned => owned.Ownership.UnlistedSince is null)
                    .ToList();
                if (inLibrary.Count == 0)
                {
                    throw new GamesException(GamesError.NotInLibrary);
                }

                var missing = inLibrary.Where(owned => owned.Ownership.MissingSince is not null).ToList();
                if (missing.Count == 0)
                {
                    throw new GamesException(GamesError.StillListed);
                }

                var now = clock.GetUtcNow();
                await library.SaveAsync(
                    [.. missing.Select(owned => owned with { Ownership = owned.Ownership with { UnlistedSince = now, RecentPlaytime = null } })],
                    ct);
                return true;
            },
            ct);

    public Task RestoreGameAsync(Guid ownerId, Guid gameId, CancellationToken ct) =>
        unitOfWork.RunAsync(
            async () =>
            {
                var removed = await RemovedAsync(ownerId, gameId, ct);
                await library.SaveAsync(
                    [
                        .. removed.Select(owned => owned with
                        {
                            Ownership = owned.Ownership with { UnlistedSince = null, ListedAgainSince = null, NoticeDismissedAt = null },
                        }),
                    ],
                    ct);
                return true;
            },
            ct);

    public Task DismissReturnAsync(Guid ownerId, Guid gameId, CancellationToken ct) =>
        unitOfWork.RunAsync(
            async () =>
            {
                var removed = await RemovedAsync(ownerId, gameId, ct);
                var back = removed.Where(owned => owned.Ownership.ListedAgainSince is not null).ToList();
                if (back.Count == 0)
                {
                    throw new GamesException(GamesError.NotBack);
                }

                // Already dismissed, and nothing back since: no notice is shown, and none is
                // silenced ahead of time for an entry that is not back.
                if (back.TrueForAll(owned => owned.Ownership.NoticeDismissedAt is not null))
                {
                    return true;
                }

                // The notice is the game's: every entry taken out, or one a store lists later
                // would raise it again.
                var now = clock.GetUtcNow();
                await library.SaveAsync(
                    [
                        .. removed.Where(owned => owned.Ownership.NoticeDismissedAt is null)
                            .Select(owned => owned with { Ownership = owned.Ownership with { NoticeDismissedAt = now } }),
                    ],
                    ct);
                return true;
            },
            ct);

    public async Task<IReadOnlyList<RemovedGame>> GetRemovedAsync(Guid ownerId, CancellationToken ct)
    {
        var games = RemovedGames.Of(await library.ListRemovedAsync(ownerId, gameId: null, ct));
        games.Sort((a, b) => Named((a.Name, a.GameId), (b.Name, b.GameId)));
        return games;
    }

    /// <summary>What an owner took out of a game.</summary>
    /// <exception cref="GamesException"><see cref="GamesError.NotRemoved"/>.</exception>
    private async Task<List<AccountOwnership>> RemovedAsync(Guid ownerId, Guid gameId, CancellationToken ct)
    {
        var removed = (await library.FindGameOwnershipsAsync(ownerId, gameId, ct))
            .Where(owned => owned.Ownership.UnlistedSince is not null)
            .ToList();
        return removed.Count > 0 ? removed : throw new GamesException(GamesError.NotRemoved);
    }

    /// <summary>An owner's library, a game once however many entries it is owned through, each with
    /// the cover its owner chose while it still is one, as the game's page says it.</summary>
    private async Task<List<OwnedGame>> GamesAsync(Guid ownerId, CancellationToken ct)
    {
        var games = OwnedGames.Of(await library.ListAsync(ownerId, ct), sources, clock.GetUtcNow());
        Guid[] chosen = [.. games.Where(game => game.CoverSource is not null).Select(game => game.GameId)];
        if (chosen.Length == 0)
        {
            return games;
        }

        var origins = await catalogue.FindCoverOriginsAsync(chosen, ct);
        return
        [
            .. games.Select(game => game.CoverSource is null
                ? game
                : game with
                {
                    CoverSource = CoverOrigins.Chosen(
                        (origins.GetValueOrDefault(game.GameId) ?? CoverOrigins.None).Sources(gameData.Source),
                        game.CoverSource),
                }),
        ];
    }

    /// <summary>The order of a library. What cannot be placed comes last, and two games the order does
    /// not tell apart come by name, then by id: a page never depends on chance.</summary>
    private static Comparison<OwnedGame> Order(LibrarySort sort)
    {
        static int ByName(OwnedGame a, OwnedGame b) => Named((a.Name, a.GameId), (b.Name, b.GameId));

        int Then(int order, OwnedGame a, OwnedGame b) => order != 0 ? order : ByName(a, b);

        return sort switch
        {
            LibrarySort.Playtime => (a, b) => Then(Descending(a.Playtime, b.Playtime), a, b),
            LibrarySort.LastPlayed => (a, b) => Then(Descending(a.LastPlayedAt, b.LastPlayedAt), a, b),
            _ => ByName,
        };
    }

    /// <summary>By name, a game without one last, then by id. Letters before their case and their
    /// accents, the same in every language of the interface.</summary>
    private static int Named((string? Name, Guid Id) a, (string? Name, Guid Id) b)
    {
        var order = (a.Name, b.Name) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => StringComparer.InvariantCultureIgnoreCase.Compare(a.Name, b.Name),
        };
        return order != 0 ? order : a.Id.CompareTo(b.Id);
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
    /// that goes down without reaching zero is the store correcting itself, and is taken. A game
    /// listed is no longer missing, unless a complete reading left it out more recently than this
    /// one: a reading that arrives late does not undo what is known since. Listing a game never
    /// brings it back into a library its owner took it out of: it is noted as listed again, since the
    /// earliest reading that did after it was taken out, and the owner decides. One date cannot say both "since the earliest reading that left
    /// it out" and "since it was last listed": readings that arrive out of order, between two that
    /// left the game out, may clear the note or date it wrongly, until the next complete reading sets
    /// it again. Nothing reads the past yet.</summary>
    private static Ownership Updated(Ownership? current, OwnedItem item, Guid entry, DateTimeOffset takenAt, Guid snapshot)
    {
        var silent = item.Playtime is null || (item.Playtime == TimeSpan.Zero && current?.Playtime > TimeSpan.Zero);
        var firstSeen = current is not null && current.FirstSeenAt < takenAt ? current.FirstSeenAt : takenAt;
        var goneSince = current?.UnlistedSince;
        var missingSince = current?.MissingSince > takenAt ? current.MissingSince : null;

        // A reading from before its owner took the game out tells them nothing new: no notice.
        DateTimeOffset? listedAgainSince = goneSince is null || missingSince is not null
            ? null
            : current?.ListedAgainSince ?? (takenAt >= goneSince ? takenAt : null);
        return new Ownership(
            entry,
            silent ? current?.Playtime : item.Playtime,
            item.LastPlayedAt ?? current?.LastPlayedAt,
            firstSeen,
            takenAt,
            snapshot,
            goneSince is null && missingSince is null ? item.RecentPlaytime : null,
            goneSince,
            missingSince,
            listedAgainSince,
            current?.NoticeDismissedAt);
    }

    /// <summary>The total a reading leaves known, for the history: <paramref name="total"/>, unless it is
    /// a zero that says nothing. A zero where a total is known is a poor answer, whenever the reading was
    /// taken: one that arrives late is no more to be believed than one that arrives in order. A zero in a
    /// reading that gives no time to any game is the store hiding the playtime. A zero beside games that
    /// have a total is a game never launched, and is where its count starts.</summary>
    private static TimeSpan? Known(TimeSpan? total, TimeSpan? before, bool saysNoTime) =>
        total == TimeSpan.Zero && (before > TimeSpan.Zero || saysNoTime) ? null : total;

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
