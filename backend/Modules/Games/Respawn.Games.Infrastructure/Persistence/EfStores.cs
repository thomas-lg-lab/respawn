using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Respawn.Games.Ports;

namespace Respawn.Games.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(GamesDbContext db) : IUnitOfWork
{
    /// <summary>The advisory lock every write to the catalogue takes; any number, as long as it is
    /// always this one and no other module's.</summary>
    private const long CatalogueLock = 7306;

    public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken ct) =>
        // Inside another run already: its transaction, its lock, and its retries.
        db.Database.CurrentTransaction is not null
            ? work()
            : RunInTransactionAsync(work, ct);

    private Task<T> RunInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct) =>
        // The context retries on a lost connection (GamesDbContext.Configure), which EF only allows for a
        // transaction when the whole of it can be run again: hence the delegate.
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // nothing of an attempt that failed half-way
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Released by PostgreSQL itself when the transaction ends, committed or not.
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({CatalogueLock})", ct);
            var result = await work();
            await transaction.CommitAsync(ct);
            return result;
        });
}

internal sealed class EfLinkedAccountStore(GamesDbContext db) : ILinkedAccountStore
{
    public async Task<LinkedAccount?> FindAsync(string source, string externalId, CancellationToken ct) =>
        await Accounts(db.LinkedAccounts.Where(a => a.Source == source && a.ExternalId == externalId)).FirstOrDefaultAsync(ct);

    public async Task<LinkedAccount?> FindByIdAsync(Guid id, CancellationToken ct) =>
        await Accounts(db.LinkedAccounts.Where(a => a.Id == id)).FirstOrDefaultAsync(ct);

    public async Task AddAsync(LinkedAccount account, CancellationToken ct)
    {
        db.LinkedAccounts.Add(new LinkedAccountRow
        {
            Id = account.Id,
            OwnerId = account.OwnerId,
            Source = account.Source,
            ExternalId = account.ExternalId,
            DisplayName = account.DisplayName,
            LinkedAt = account.LinkedAt,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LinkedAccount>> ListAsync(Guid ownerId, CancellationToken ct) =>
        await Accounts(db.LinkedAccounts.Where(a => a.OwnerId == ownerId)).ToListAsync(ct);

    public async Task<IReadOnlyList<LinkedAccount>> ListAllAsync(CancellationToken ct) =>
        await Accounts(db.LinkedAccounts).ToListAsync(ct);

    public async Task RemoveAsync(Guid id, CancellationToken ct)
    {
        // In the order the foreign keys allow: what points to a snapshot before the snapshots, and
        // the account last. Nothing here is deleted along with something else by the database.
        await db.SyncRuns.Where(r => r.LinkedAccountId == id).ExecuteDeleteAsync(ct);
        await db.MonthlyPlaytime.Where(m => m.LinkedAccountId == id).ExecuteDeleteAsync(ct);
        await db.YearReviews.Where(r => r.LinkedAccountId == id).ExecuteDeleteAsync(ct);
        await db.Ownerships.Where(o => o.LinkedAccountId == id).ExecuteDeleteAsync(ct);
        await db.PlaytimeGains.Where(g => g.LinkedAccountId == id).ExecuteDeleteAsync(ct);
        await db.Observations
            .Where(o => db.RawSnapshots.Any(s => s.Id == o.SnapshotId && s.LinkedAccountId == id))
            .ExecuteDeleteAsync(ct);
        await db.RawSnapshots.Where(s => s.LinkedAccountId == id).ExecuteDeleteAsync(ct);
        await db.LinkedAccounts.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
    }

    /// <summary>Linked accounts as the core knows them. Whether a key was given is asked of the database:
    /// the sealed key itself is only read when a sync opens it.</summary>
    private static IQueryable<LinkedAccount> Accounts(IQueryable<LinkedAccountRow> rows) =>
        rows.AsNoTracking().Select(row => new LinkedAccount(
            row.Id, row.OwnerId, row.Source, row.ExternalId, row.DisplayName, row.LinkedAt, row.Credential != null));
}

internal sealed class EfSnapshotStore(GamesDbContext db) : ISnapshotStore
{
    public async Task AddAsync(RawSnapshot snapshot, CancellationToken ct)
    {
        db.RawSnapshots.Add(new RawSnapshotRow
        {
            Id = snapshot.Id,
            Source = snapshot.Source,
            Kind = snapshot.Kind,
            TakenAt = snapshot.TakenAt,
            RecordedAt = snapshot.RecordedAt,
            LinkedAccountId = snapshot.LinkedAccountId,
            Complete = snapshot.Complete,
            Payload = snapshot.Payload,
        });
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class EfSyncRunStore(GamesDbContext db) : ISyncRunStore
{
    public async Task AddAsync(SyncRun run, CancellationToken ct)
    {
        db.SyncRuns.Add(new SyncRunRow
        {
            Id = run.Id,
            LinkedAccountId = run.LinkedAccountId,
            Trigger = SyncWords.Of(run.Trigger),
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            Outcome = SyncWords.Of(run.Outcome),
            Error = run.Error is { } error ? SyncWords.Of(error) : null,
            Detail = run.Detail,
            SnapshotId = run.SnapshotId,
            OwnCredential = run.OwnCredential,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SyncRun>> ListSinceAsync(DateTimeOffset since, CancellationToken ct) =>
        [.. (await db.SyncRuns.AsNoTracking().Where(r => r.StartedAt >= since).ToListAsync(ct)).Select(ToRun)];

    public Task<SyncRun?> FindLatestAsync(Guid linkedAccountId, CancellationToken ct) =>
        LatestAsync(db.SyncRuns.Where(r => r.LinkedAccountId == linkedAccountId), ct);

    public async Task<IReadOnlyDictionary<Guid, LatestRuns>> FindLatestAsync(IReadOnlyCollection<Guid> linkedAccountIds, CancellationToken ct)
    {
        // One query; each account's two runs come from an index, however many runs it has.
        Guid[] ids = [.. linkedAccountIds];
        var succeeded = SyncWords.Of(SyncOutcome.Succeeded);
        var latest = await db.LinkedAccounts.AsNoTracking()
            .Where(account => ids.Contains(account.Id))
            .Select(account => new
            {
                account.Id,
                Last = db.SyncRuns
                    .Where(r => r.LinkedAccountId == account.Id)
                    .OrderByDescending(r => r.StartedAt)
                    .ThenByDescending(r => r.Id)
                    .FirstOrDefault(),
                LastSucceeded = db.SyncRuns
                    .Where(r => r.LinkedAccountId == account.Id && r.Outcome == succeeded)
                    .OrderByDescending(r => r.StartedAt)
                    .ThenByDescending(r => r.Id)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);
        return latest.ToDictionary(
            runs => runs.Id,
            runs => new LatestRuns(
                runs.Last is { } last ? ToRun(last) : null,
                runs.LastSucceeded is { } success ? ToRun(success) : null));
    }

    private static async Task<SyncRun?> LatestAsync(IQueryable<SyncRunRow> runs, CancellationToken ct) =>
        await runs.AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct) is { } row
            ? ToRun(row)
            : null;

    private static SyncRun ToRun(SyncRunRow row) =>
        new(
            row.Id,
            row.LinkedAccountId,
            SyncWords.Trigger(row.Trigger),
            row.StartedAt,
            row.FinishedAt,
            SyncWords.Outcome(row.Outcome),
            row.Error is { } error ? SyncWords.Error(error) : null,
            row.Detail,
            row.SnapshotId,
            row.OwnCredential);
}

/// <summary>The words a sync run is stored in: the table's own, which a renamed enum must not change.</summary>
internal static class SyncWords
{
    public const int MaxLength = 32;

    public static string Of(SyncTrigger trigger) => trigger switch
    {
        SyncTrigger.Scheduled => "scheduled",
        SyncTrigger.Manual => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, null),
    };

    public static string Of(SyncOutcome outcome) => outcome switch
    {
        SyncOutcome.Succeeded => "succeeded",
        SyncOutcome.NothingVisible => "nothing_visible",
        SyncOutcome.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    public static string Of(SourceError error) => error switch
    {
        SourceError.NotConfigured => "not_configured",
        SourceError.Refused => "refused",
        SourceError.Unavailable => "unavailable",
        SourceError.Unreadable => "unreadable",
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };

    public static SyncTrigger Trigger(string word) => word switch
    {
        "scheduled" => SyncTrigger.Scheduled,
        "manual" => SyncTrigger.Manual,
        _ => throw new InvalidOperationException($"not a sync trigger: {word}"),
    };

    public static SyncOutcome Outcome(string word) => word switch
    {
        "succeeded" => SyncOutcome.Succeeded,
        "nothing_visible" => SyncOutcome.NothingVisible,
        "failed" => SyncOutcome.Failed,
        _ => throw new InvalidOperationException($"not a sync outcome: {word}"),
    };

    public static SourceError Error(string word) => word switch
    {
        "not_configured" => SourceError.NotConfigured,
        "refused" => SourceError.Refused,
        "unavailable" => SourceError.Unavailable,
        "unreadable" => SourceError.Unreadable,
        _ => throw new InvalidOperationException($"not a sync error: {word}"),
    };
}

internal sealed class EfCatalogue(GamesDbContext db) : ICatalogue
{
    public async Task<IReadOnlyDictionary<string, StoreEntry>> FindStoreEntriesAsync(
        string source, IReadOnlyCollection<string> externalIds, CancellationToken ct)
    {
        string[] ids = [.. externalIds];
        return await db.StoreEntries.AsNoTracking()
            .Where(e => e.Source == source && ids.Contains(e.ExternalId))
            .ToDictionaryAsync(e => e.ExternalId, e => new StoreEntry(e.Id, e.Name), StringComparer.Ordinal, ct);
    }

    public async Task AddAsync(IReadOnlyCollection<NewStoreEntry> entries, DateTimeOffset at, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            db.Games.Add(new GameRow { Id = entry.GameId, Name = entry.Name, NameSourceEntryId = entry.Id, CreatedAt = at });
            db.StoreEntries.Add(new StoreEntryRow
            {
                Id = entry.Id,
                GameId = entry.GameId,
                Source = entry.Source,
                ExternalId = entry.ExternalId,
                Name = entry.Name,
                CreatedAt = at,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task RenameAsync(IReadOnlyCollection<StoreEntryRename> renames, CancellationToken ct)
    {
        Guid[] ids = [.. renames.Select(rename => rename.Id)];
        var entries = await db.StoreEntries.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        Guid[] gameIds = [.. entries.Values.Select(e => e.GameId).Distinct()];
        var games = await db.Games.Where(g => gameIds.Contains(g.Id) && g.NameSourceEntryId != null).ToDictionaryAsync(g => g.Id, ct);
        foreach (var rename in renames)
        {
            var entry = entries[rename.Id];
            entry.Name = rename.Name;
            // The record follows the entry it took its name from, and that one only. After a merge the
            // other entries that point to it are renamed alone, even when they bore the same name.
            if (games.TryGetValue(entry.GameId, out var game) && game.NameSourceEntryId == entry.Id)
            {
                game.Name = rename.Name;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<CatalogueGame?> FindGameAsync(Guid gameId, CancellationToken ct)
    {
        var game = await db.Games.AsNoTracking().Where(g => g.Id == gameId).Select(g => new { g.Name }).FirstOrDefaultAsync(ct);
        if (game is null)
        {
            return null;
        }

        var entries = await db.StoreEntries.AsNoTracking().Where(e => e.GameId == gameId)
            .Select(e => new GameStore(e.Id, e.Source, e.ExternalId))
            .ToListAsync(ct);
        return new CatalogueGame(gameId, game.Name, entries);
    }

    public async Task<IReadOnlyDictionary<Guid, GameFacts>> FindGameFactsAsync(IReadOnlyCollection<Guid> gameIds, CancellationToken ct)
    {
        Guid[] ids = [.. gameIds];
        var rows = await (
            from e in db.StoreEntries.AsNoTracking().Where(e => ids.Contains(e.GameId))
            join g in db.IgdbGames.AsNoTracking() on e.IgdbId equals (long?)g.IgdbId
            select new { e.GameId, e.Source, EntryId = e.Id, Game = g })
            .ToListAsync(ct);

        // The first entry the database knows, in the order the covers are looked for: one entry's
        // answer whole, never fields mixed from two.
        return rows
            .GroupBy(row => row.GameId)
            .ToDictionary(
                game => game.Key,
                game => Facts(game.OrderBy(row => row.Source, StringComparer.Ordinal).ThenBy(row => row.EntryId).First().Game));
    }

    private static GameFacts Facts(IgdbGameRow game)
    {
        // A duration of nothing says nothing: kept as it came, never shown as a pace.
        int? hastily = Said(game.HastilySeconds), normally = Said(game.NormallySeconds), completely = Said(game.CompletelySeconds);
        return new(
            game.IgdbId.ToString(CultureInfo.InvariantCulture),
            game.Name,
            game.Slug,
            game.Url,
            game.CoverImageId,
            game.Genres,
            game.Themes,
            game.Summary,
            hastily is null && normally is null && completely is null
                ? null
                : new TimeToBeat(hastily, normally, completely, game.TimeToBeatCount),
            game.ReleasedOn,
            [.. game.Similar.Select(id => id.ToString(CultureInfo.InvariantCulture))]);

        static int? Said(int? seconds) => seconds > 0 ? seconds : null;
    }

    public async Task<GameDataCover> FindGameDataCoverAsync(Guid gameId, CancellationToken ct)
    {
        var entries = await GameDataEntries(gameId).ToListAsync(ct);
        return new GameDataCover(
            First(entries, e => e.CoverImageId),
            entries.All(e => e.IgdbAskedAt != null),
            entries.SelectMany(e => new[] { e.IgdbAskedAt, e.ReadAt }).Max());
    }

    public async Task<IReadOnlyDictionary<Guid, CoverOrigins>> FindCoverOriginsAsync(IReadOnlyCollection<Guid> gameIds, CancellationToken ct)
    {
        Guid[] ids = [.. gameIds];
        var rows = await (
            from e in db.StoreEntries.AsNoTracking().Where(e => ids.Contains(e.GameId))
            join g in db.IgdbGames.AsNoTracking() on e.IgdbId equals (long?)g.IgdbId into said
            from g in said.DefaultIfEmpty()
            select new { e.GameId, e.Source, HasPath = e.CoverPath != null, e.CoverAskedAt, HasData = g != null && g.CoverImageId != null })
            .ToListAsync(ct);
        var shown = await db.Images.AsNoTracking()
            .Where(i => ids.Contains(i.GameId) && i.Kind == ImageKinds.Cover && i.Slot == ImageSlots.Own)
            .Select(i => new { i.GameId, Shown = new ShownCover(i.Source, i.CheckedAt) })
            .ToDictionaryAsync(i => i.GameId, i => i.Shown, ct);
        return rows.GroupBy(row => row.GameId).ToDictionary(
            game => game.Key,
            game => new CoverOrigins(
                [.. game.Where(row => row.HasPath).Select(row => new StoreCoverSaid(row.Source, row.CoverAskedAt))],
                game.Any(row => row.HasData),
                shown.GetValueOrDefault(game.Key)));
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<Guid>>> FindGamesOfGameDataAsync(IReadOnlyCollection<string> gameDataIds, CancellationToken ct)
    {
        long[] ids = [.. gameDataIds.Select(IgdbNumber).OfType<long>().Distinct()];
        if (ids.Length == 0)
        {
            return new Dictionary<string, IReadOnlyList<Guid>>();
        }

        var rows = await db.StoreEntries.AsNoTracking()
            .Where(e => e.IgdbId != null && ids.Contains(e.IgdbId.Value))
            .Select(e => new { IgdbId = e.IgdbId!.Value, e.GameId })
            .Distinct()
            .ToListAsync(ct);
        return rows
            .GroupBy(row => row.IgdbId)
            .ToDictionary(
                game => game.Key.ToString(CultureInfo.InvariantCulture),
                IReadOnlyList<Guid> (game) => [.. game.Select(row => row.GameId).Order()]);
    }

    public Task<bool> HasGameDataAsync(CancellationToken ct) => db.IgdbGames.AnyAsync(ct);

    /// <summary>A game's store entries, each with what the game database said of its game.</summary>
    private IQueryable<GameDataEntryRow> GameDataEntries(Guid gameId) =>
        from e in db.StoreEntries.AsNoTracking().Where(e => e.GameId == gameId)
        join g in db.IgdbGames.AsNoTracking() on e.IgdbId equals (long?)g.IgdbId into said
        from g in said.DefaultIfEmpty()
        select new GameDataEntryRow(
            e.Id,
            e.Source,
            e.ExternalId,
            e.IgdbAskedAt,
            g == null ? null : g.CoverImageId,
            g == null ? null : (DateTimeOffset?)g.ReadAt);

    /// <summary>The first value of the entries, in the order the covers are looked for: by store, then
    /// by entry, the same every time.</summary>
    private static string? First(List<GameDataEntryRow> entries, Func<GameDataEntryRow, string?> value) =>
        entries.OrderBy(e => e.Source, StringComparer.Ordinal).ThenBy(e => e.Id).Select(value).FirstOrDefault(found => found is not null);

    private sealed record GameDataEntryRow(
        Guid Id,
        string Source,
        string ExternalId,
        DateTimeOffset? IgdbAskedAt,
        string? CoverImageId,
        DateTimeOffset? ReadAt);

    public async Task<IReadOnlyDictionary<Guid, string?>> FindNamesAsync(IReadOnlyCollection<Guid> gameIds, CancellationToken ct)
    {
        Guid[] ids = [.. gameIds];
        return await db.Games.AsNoTracking().Where(g => ids.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
    }

    public async Task<IReadOnlyList<CoverEntry>?> FindCoverEntriesAsync(Guid gameId, CancellationToken ct)
    {
        var entries = await db.StoreEntries.AsNoTracking()
            .Where(e => e.GameId == gameId)
            .Select(e => new CoverEntry(e.Id, e.Source, e.ExternalId, e.CoverPath, e.CoverAskedAt))
            .ToListAsync(ct);

        // A game most often has an entry: the second question is for the id that is no game's.
        return entries.Count > 0 || await db.Games.AnyAsync(g => g.Id == gameId, ct) ? entries : null;
    }

    public async Task<IReadOnlyList<Guid>> ListAccountsWithCoverPathsDueAsync(DateTimeOffset askedBefore, CancellationToken ct) =>
        await db.Ownerships.AsNoTracking()
            .Join(db.StoreEntries.AsNoTracking(), o => o.StoreEntryId, e => e.Id, (o, e) => new { o.LinkedAccountId, e.CoverAskedAt })
            .Where(row => row.CoverAskedAt == null || row.CoverAskedAt < askedBefore)
            .Select(row => row.LinkedAccountId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CoverEntry>> ListCoverPathsDueAsync(Guid linkedAccountId, DateTimeOffset askedBefore, CancellationToken ct) =>
        await db.Ownerships.AsNoTracking()
            .Where(o => o.LinkedAccountId == linkedAccountId)
            .Join(db.StoreEntries.AsNoTracking(), o => o.StoreEntryId, e => e.Id, (o, e) => e)
            .Where(e => e.CoverAskedAt == null || e.CoverAskedAt < askedBefore)
            .OrderBy(e => e.Id)
            .Select(e => new CoverEntry(e.Id, e.Source, e.ExternalId, e.CoverPath, e.CoverAskedAt))
            .ToListAsync(ct);

    public async Task SetCoverPathsAsync(IReadOnlyCollection<CoverPath> paths, DateTimeOffset at, CancellationToken ct)
    {
        Guid[] ids = [.. paths.Select(path => path.StoreEntryId)];
        var entries = await db.StoreEntries.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        foreach (var path in paths)
        {
            if (entries.TryGetValue(path.StoreEntryId, out var entry))
            {
                entry.CoverPath = path.Path;
                entry.CoverAskedAt = at;
            }
        }

        await SaveAsync(ct);
    }

    public async Task<KeptImage?> FindImageAsync(Guid gameId, string kind, string slot, CancellationToken ct) =>
        await db.Images.AsNoTracking()
            .Where(i => i.GameId == gameId && i.Kind == kind && i.Slot == slot)
            .Select(i => new KeptImage(new ImageName(i.Kind, i.Source, i.Key, i.ContentType), i.CheckedAt))
            .FirstOrDefaultAsync(ct);

    public async Task<ImageName?> KeepImageAsync(Guid gameId, string slot, KeptImage image, CancellationToken ct)
    {
        var name = image.Name;
        var row = await db.Images.FirstOrDefaultAsync(i => i.GameId == gameId && i.Kind == name.Kind && i.Slot == slot, ct);
        var replaced = row is null ? null : new ImageName(row.Kind, row.Source, row.Key, row.ContentType);
        if (row is null)
        {
            row = new ImageRow { GameId = gameId, Kind = name.Kind, Slot = slot };
            db.Images.Add(row);
        }

        (row.Source, row.Key, row.ContentType, row.CheckedAt) = (name.Source, name.Key, name.ContentType, image.CheckedAt);
        await SaveAsync(ct);
        return replaced == name ? null : replaced;
    }

    // Two games, or two slots of one, may show one file: it goes only once none does.
    public Task<bool> IsImageShownAsync(ImageName image, CancellationToken ct) =>
        db.Images.AnyAsync(i => i.Kind == image.Kind && i.Source == image.Source && i.Key == image.Key, ct);

    public Task CheckedImageAsync(Guid gameId, string kind, string slot, DateTimeOffset at, CancellationToken ct) =>
        db.Images.Where(i => i.GameId == gameId && i.Kind == kind && i.Slot == slot)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.CheckedAt, at), ct);

    public async Task<IReadOnlyList<string>> ListStoresWithGameDataDueAsync(IReadOnlyCollection<string> stores, GameDataDue due, CancellationToken ct)
    {
        string[] asked = [.. stores];
        return await GameDataDue(db.StoreEntries.AsNoTracking().Where(e => asked.Contains(e.Source)), due)
            .Select(e => e.Source)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<GameDataEntry>> ListGameDataDueAsync(string store, GameDataDue due, int max, CancellationToken ct) =>
        await GameDataDue(db.StoreEntries.AsNoTracking().Where(e => e.Source == store), due)
            // Never asked first; then the oldest asked.
            .OrderBy(e => e.IgdbAskedAt != null)
            .ThenBy(e => e.IgdbAskedAt)
            .ThenBy(e => e.Id)
            .Take(max)
            .Select(e => new GameDataEntry(e.Id, e.ExternalId))
            .ToListAsync(ct);

    private static IQueryable<StoreEntryRow> GameDataDue(IQueryable<StoreEntryRow> entries, GameDataDue due) =>
        entries.Where(e => e.IgdbAskedAt == null
            || (e.IgdbId != null && e.IgdbAskedAt < due.MatchedBefore)
            || (e.IgdbId == null && e.IgdbAskedAt < due.UnknownBefore));

    public async Task SetGameDataAsync(IReadOnlyCollection<GameFacts> games, IReadOnlyCollection<GameDataMatch> matches, DateTimeOffset at, CancellationToken ct)
    {
        var said = games
            .Select(game => (Id: IgdbNumber(game.GameId), Game: game))
            .Where(game => game.Id is not null)
            .GroupBy(game => game.Id!.Value)
            .ToDictionary(group => group.Key, group => group.First().Game);
        long[] ids = [.. said.Keys];
        var kept = await db.IgdbGames.Where(g => ids.Contains(g.IgdbId)).ToDictionaryAsync(g => g.IgdbId, ct);
        foreach (var (id, game) in said)
        {
            if (!kept.TryGetValue(id, out var row))
            {
                row = new IgdbGameRow { IgdbId = id };
                db.IgdbGames.Add(row);
            }

            row.Name = game.Name;
            row.Slug = game.Slug;
            row.Url = game.Url;
            row.CoverImageId = game.CoverImageId;
            row.Genres = [.. game.Genres];
            row.Themes = [.. game.Themes];
            row.Summary = game.Summary;
            row.HastilySeconds = game.TimeToBeat?.Hastily;
            row.NormallySeconds = game.TimeToBeat?.Normally;
            row.CompletelySeconds = game.TimeToBeat?.Completely;
            row.TimeToBeatCount = game.TimeToBeat?.Count;
            row.ReleasedOn = game.ReleasedOn;
            row.Similar = [.. (game.Similar ?? []).Select(IgdbNumber).OfType<long>()];
            row.ReadAt = at;
        }

        Guid[] entryIds = [.. matches.Select(match => match.StoreEntryId)];
        var entries = await db.StoreEntries.Where(e => entryIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var left = new HashSet<long>();
        foreach (var match in matches)
        {
            if (entries.TryGetValue(match.StoreEntryId, out var entry))
            {
                var igdbId = match.GameId is null ? null : IgdbNumber(match.GameId);
                if (entry.IgdbId is { } before && before != igdbId)
                {
                    left.Add(before);
                }

                entry.IgdbId = igdbId;
                entry.IgdbAskedAt = at;
            }
        }

        await SaveAsync(ct);

        // The games no store entry is any more, once these changed theirs: nothing would ask about them again.
        if (left.Count > 0)
        {
            long[] forgotten = [.. left];
            await db.IgdbGames
                .Where(g => forgotten.Contains(g.IgdbId) && !db.StoreEntries.Any(e => e.IgdbId == g.IgdbId))
                .ExecuteDeleteAsync(ct);
        }
    }

    public async Task<IReadOnlyList<string>> ListStoresWithStoreTagsDueAsync(IReadOnlyCollection<string> stores, DateTimeOffset askedBefore, CancellationToken ct)
    {
        string[] asked = [.. stores];
        return await db.StoreEntries.AsNoTracking()
            .Where(e => asked.Contains(e.Source) && (e.TagsAskedAt == null || e.TagsAskedAt < askedBefore))
            .Select(e => e.Source)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StoreTagEntry>> ListStoreTagsDueAsync(string store, DateTimeOffset askedBefore, int max, CancellationToken ct) =>
        await db.StoreEntries.AsNoTracking()
            .Where(e => e.Source == store && (e.TagsAskedAt == null || e.TagsAskedAt < askedBefore))
            // Never asked first; then the oldest asked.
            .OrderBy(e => e.TagsAskedAt != null)
            .ThenBy(e => e.TagsAskedAt)
            .ThenBy(e => e.Id)
            .Take(max)
            .Select(e => new StoreTagEntry(e.Id, e.ExternalId))
            .ToListAsync(ct);

    public async Task SetStoreTagsAsync(string store, IReadOnlyCollection<StoreTagName> names, IReadOnlyCollection<StoreEntryTags> tags, DateTimeOffset at, CancellationToken ct)
    {
        if (names.Count > 0)
        {
            // The store's names whole, in the place of those it gave before: a tag it renamed or dropped
            // takes its new name, or none.
            await db.StoreTagNames.Where(n => n.Source == store).ExecuteDeleteAsync(ct);
            db.StoreTagNames.AddRange(
                names
                    .Where(name => name.Id.Length is > 0 and <= Limits.StoreTagId)
                    .DistinctBy(name => name.Id, StringComparer.Ordinal)
                    .SelectMany(name => name.Names
                        .Where(said => StoreTagSchedule.Languages.Contains(said.Key) && said.Value.Length is > 0 and <= Limits.StoreTagName)
                        .Select(said => new StoreTagNameRow { Source = store, TagId = name.Id, Language = said.Key, Name = said.Value, ReadAt = at })));
        }

        Guid[] ids = [.. tags.Select(entry => entry.StoreEntryId)];
        var entries = await db.StoreEntries.Where(e => ids.Contains(e.Id) && e.Source == store).ToDictionaryAsync(e => e.Id, ct);
        // Only the entries the store said something of lose the tags they had; the others keep theirs.
        Guid[] replaced = [.. tags.Where(entry => entry.Tags is not null && entries.ContainsKey(entry.StoreEntryId)).Select(entry => entry.StoreEntryId)];
        await db.StoreEntryTags.Where(t => replaced.Contains(t.StoreEntryId)).ExecuteDeleteAsync(ct);
        foreach (var entry in tags)
        {
            if (!entries.TryGetValue(entry.StoreEntryId, out var row))
            {
                continue;
            }

            row.TagsAskedAt = at;
            if (entry.Tags is null)
            {
                continue;
            }

            db.StoreEntryTags.AddRange(
                entry.Tags
                    .Where(tag => tag.Id.Length is > 0 and <= Limits.StoreTagId)
                    .DistinctBy(tag => tag.Id, StringComparer.Ordinal)
                    .Select((tag, rank) => new StoreEntryTagRow { StoreEntryId = row.Id, TagId = tag.Id, Weight = tag.Weight, Rank = rank }));
        }

        await SaveAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> FindStoreTagsAsync(IReadOnlyCollection<Guid> gameIds, CancellationToken ct)
    {
        Guid[] ids = [.. gameIds];
        var rows = await (
            from e in db.StoreEntries.AsNoTracking().Where(e => ids.Contains(e.GameId))
            join t in db.StoreEntryTags.AsNoTracking() on e.Id equals t.StoreEntryId
            select new { e.GameId, e.Source, t.TagId, t.Rank })
            .ToListAsync(ct);

        // Over the game's entries, a tag once, at its best place on any of them.
        return rows
            .GroupBy(row => row.GameId)
            .ToDictionary(
                game => game.Key,
                game => (IReadOnlyList<string>)
                [
                    .. game
                        .GroupBy(row => StoreTagKeys.Of(row.Source, row.TagId), StringComparer.Ordinal)
                        .OrderBy(tag => tag.Min(row => row.Rank))
                        .ThenBy(tag => tag.Key, StringComparer.Ordinal)
                        .Select(tag => tag.Key),
                ]);
    }

    public async Task<DateTimeOffset?> FindStoreTagNamesReadAtAsync(string store, CancellationToken ct) =>
        await db.StoreTagNames.AsNoTracking().Where(n => n.Source == store).MaxAsync(n => (DateTimeOffset?)n.ReadAt, ct);

    public async Task<IReadOnlyList<NamedStoreTag>> ListStoreTagNamesAsync(CancellationToken ct)
    {
        var rows = await db.StoreTagNames.AsNoTracking().ToListAsync(ct);
        return
        [
            .. rows
                .GroupBy(row => StoreTagKeys.Of(row.Source, row.TagId), StringComparer.Ordinal)
                .Select(tag => new NamedStoreTag(
                    tag.Key,
                    tag.ToDictionary(row => row.Language, row => row.Name, StringComparer.Ordinal))),
        ];
    }

    /// <summary>An IGDB game's id as the table keeps it; null for what is not one.</summary>
    private static long? IgdbNumber(string id) =>
        long.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : null;

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // What could not be kept is not left to be written by whatever saves next in this scope.
            db.ChangeTracker.Clear();
            throw;
        }
    }
}

internal sealed class EfYearReviewStore(GamesDbContext db) : IYearReviewStore
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlySet<int>>> ListYearsAsync(IReadOnlyCollection<Guid> linkedAccountIds, CancellationToken ct)
    {
        Guid[] ids = [.. linkedAccountIds];
        var rows = await db.YearReviews.AsNoTracking()
            .Where(r => ids.Contains(r.LinkedAccountId))
            .Select(r => new { r.LinkedAccountId, r.Year })
            .ToListAsync(ct);
        return rows.GroupBy(row => row.LinkedAccountId)
            .ToDictionary(account => account.Key, IReadOnlySet<int> (account) => account.Select(row => row.Year).ToHashSet());
    }

    public async Task RecordAsync(Guid linkedAccountId, int year, Guid snapshotId, IReadOnlyCollection<MonthPlayed> months, CancellationToken ct)
    {
        var (first, next) = (new DateOnly(year, 1, 1), new DateOnly(year + 1, 1, 1));
        await db.MonthlyPlaytime
            .Where(m => m.LinkedAccountId == linkedAccountId && m.Month >= first && m.Month < next)
            .ExecuteDeleteAsync(ct);
        await db.YearReviews.Where(r => r.LinkedAccountId == linkedAccountId && r.Year == year).ExecuteDeleteAsync(ct);

        db.YearReviews.Add(new YearReviewRow { LinkedAccountId = linkedAccountId, Year = year, SnapshotId = snapshotId });
        db.MonthlyPlaytime.AddRange(months.Select(month => new MonthlyPlaytimeRow
        {
            LinkedAccountId = linkedAccountId,
            StoreEntryId = month.StoreEntryId,
            Month = month.Month,
            PlaytimeSeconds = (long)month.Playtime.TotalSeconds,
            Sessions = month.Sessions,
            SnapshotId = snapshotId,
        }));
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<(Guid LinkedAccountId, int Year)>> ListOwnerYearsAsync(Guid ownerId, CancellationToken ct) =>
        [
            .. (await (
                from review in db.YearReviews.AsNoTracking()
                join account in db.LinkedAccounts on review.LinkedAccountId equals account.Id
                where account.OwnerId == ownerId
                select new { review.LinkedAccountId, review.Year })
                .ToListAsync(ct))
                .Select(row => (row.LinkedAccountId, row.Year)),
        ];

    public async Task<IReadOnlyList<PlayedMonth>> ListPlayedAsync(Guid ownerId, Guid? gameId, DateOnly? month, CancellationToken ct)
    {
        // A condition is added only when it is asked for, so that each query is planned as it is
        // written: one month comes from the primary key (the account, then the month), one game from
        // the index on the store entry.
        var played =
            from row in db.MonthlyPlaytime.AsNoTracking()
            join account in db.LinkedAccounts on row.LinkedAccountId equals account.Id
            where account.OwnerId == ownerId
            join entry in db.StoreEntries on row.StoreEntryId equals entry.Id
            select new { row.Month, entry.GameId, row.PlaytimeSeconds };
        if (gameId is { } game)
        {
            played = played.Where(row => row.GameId == game);
        }

        if (month is { } asked)
        {
            played = played.Where(row => row.Month == asked);
        }

        var rows = await played
            .GroupBy(row => new { row.Month, row.GameId })
            .Select(group => new { group.Key.Month, group.Key.GameId, PlaytimeSeconds = group.Sum(row => row.PlaytimeSeconds) })
            .ToListAsync(ct);
        return [.. rows.Select(row => new PlayedMonth(row.Month, row.GameId, TimeSpan.FromSeconds(row.PlaytimeSeconds)))];
    }
}

internal sealed class EfLibraryStore(GamesDbContext db) : ILibraryStore
{
    public async Task<IReadOnlyDictionary<Guid, Ownership>> FindOwnershipsAsync(Guid linkedAccountId, CancellationToken ct)
    {
        var rows = await db.Ownerships.AsNoTracking().Where(o => o.LinkedAccountId == linkedAccountId).ToListAsync(ct);
        return rows.ToDictionary(row => row.StoreEntryId, Known);
    }

    public async Task<IReadOnlyList<AccountOwnership>> FindGameOwnershipsAsync(Guid ownerId, Guid gameId, CancellationToken ct)
    {
        var rows = await (
            from ownership in db.Ownerships.AsNoTracking()
            join account in db.LinkedAccounts on ownership.LinkedAccountId equals account.Id
            where account.OwnerId == ownerId
            join entry in db.StoreEntries on ownership.StoreEntryId equals entry.Id
            where entry.GameId == gameId
            select ownership)
            .ToListAsync(ct);
        return [.. rows.Select(row => new AccountOwnership(row.LinkedAccountId, Known(row)))];
    }

    public async Task SaveAsync(IReadOnlyCollection<AccountOwnership> owned, CancellationToken ct)
    {
        // One query for every row: what the pairs ask for is picked out of the few rows the two lists
        // of ids select.
        var accounts = owned.Select(o => o.LinkedAccountId).Distinct().ToList();
        var entries = owned.Select(o => o.Ownership.StoreEntryId).Distinct().ToList();
        var rows = await db.Ownerships
            .Where(o => accounts.Contains(o.LinkedAccountId) && entries.Contains(o.StoreEntryId))
            .ToDictionaryAsync(o => (o.LinkedAccountId, o.StoreEntryId), ct);
        foreach (var (linkedAccountId, ownership) in owned)
        {
            Set(rows[(linkedAccountId, ownership.StoreEntryId)], ownership);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RemovedEntry>> ListRemovedAsync(Guid ownerId, Guid? gameId, CancellationToken ct)
    {
        var removed =
            from ownership in db.Ownerships.AsNoTracking()
            join account in db.LinkedAccounts on ownership.LinkedAccountId equals account.Id
            where account.OwnerId == ownerId && ownership.UnlistedSince != null
            join entry in db.StoreEntries on ownership.StoreEntryId equals entry.Id
            join game in db.Games on entry.GameId equals game.Id
            select new
            {
                GameId = game.Id,
                GameName = game.Name,
                StoreEntryId = entry.Id,
                LinkedAccountId = account.Id,
                entry.Source,
                entry.ExternalId,
                ownership.UnlistedSince,
                ownership.ListedAgainSince,
                ownership.NoticeDismissedAt,
            };
        if (gameId is { } asked)
        {
            removed = removed.Where(row => row.GameId == asked);
        }

        var rows = await removed.ToListAsync(ct);
        return [.. rows.Select(row => new RemovedEntry(
            row.GameId,
            row.GameName,
            row.StoreEntryId,
            row.LinkedAccountId,
            row.Source,
            row.ExternalId,
            row.UnlistedSince!.Value,
            row.ListedAgainSince,
            row.NoticeDismissedAt))];
    }

    private static Ownership Known(OwnershipRow row) =>
        new(
            row.StoreEntryId,
            Playtime(row.PlaytimeSeconds),
            row.LastPlayedAt,
            row.FirstSeenAt,
            row.AsOf,
            row.SnapshotId,
            Playtime(row.RecentPlaytimeSeconds),
            row.UnlistedSince,
            row.MissingSince,
            row.ListedAgainSince,
            row.NoticeDismissedAt);

    private static void Set(OwnershipRow row, Ownership ownership)
    {
        row.PlaytimeSeconds = Seconds(ownership.Playtime);
        row.LastPlayedAt = ownership.LastPlayedAt;
        row.FirstSeenAt = ownership.FirstSeenAt;
        row.AsOf = ownership.AsOf;
        row.SnapshotId = ownership.SnapshotId;
        row.RecentPlaytimeSeconds = Seconds(ownership.RecentPlaytime);
        row.UnlistedSince = ownership.UnlistedSince;
        row.MissingSince = ownership.MissingSince;
        row.ListedAgainSince = ownership.ListedAgainSince;
        row.NoticeDismissedAt = ownership.NoticeDismissedAt;
    }

    public async Task RecordAsync(
        Guid linkedAccountId,
        Guid snapshotId,
        IReadOnlyCollection<Observation> said,
        IReadOnlyCollection<Ownership> owned,
        CancellationToken ct)
    {
        db.Observations.AddRange(said.Select(observation => new ObservationRow
        {
            SnapshotId = snapshotId,
            StoreEntryId = observation.StoreEntryId,
            PlaytimeSeconds = Seconds(observation.Playtime),
            KnownPlaytimeSeconds = Seconds(observation.KnownPlaytime),
            RecentPlaytimeSeconds = Seconds(observation.RecentPlaytime),
            LastPlayedAt = observation.LastPlayedAt,
        }));

        Guid[] entries = [.. owned.Select(ownership => ownership.StoreEntryId)];
        var rows = await db.Ownerships
            .Where(o => o.LinkedAccountId == linkedAccountId && entries.Contains(o.StoreEntryId))
            .ToDictionaryAsync(o => o.StoreEntryId, ct);
        foreach (var ownership in owned)
        {
            if (!rows.TryGetValue(ownership.StoreEntryId, out var row))
            {
                row = new OwnershipRow { LinkedAccountId = linkedAccountId, StoreEntryId = ownership.StoreEntryId };
                db.Ownerships.Add(row);
            }

            Set(row, ownership);
        }

        await db.SaveChangesAsync(ct);

        // The gains of this reading and of every one after it: a reading older than what is known
        // changes what the next one gained. A reading of today rewrites its own rows only (about 0.2 s
        // for 2,000 games and three years of daily readings); one dated before the whole history
        // rewrites all of it (about 4 s there), inside the transaction that holds the catalogue lock.
        await db.Database.ExecuteSqlRawAsync(ClearGainsSince, [linkedAccountId, snapshotId], ct);
        await WithoutJitAsync(() => db.Database.ExecuteSqlRawAsync(GainsSince, [linkedAccountId, snapshotId], ct), ct);
    }

    public async Task<IReadOnlyList<OwnedEntry>> ListAsync(Guid ownerId, CancellationToken ct)
    {
        var rows = await (
            from ownership in db.Ownerships.AsNoTracking()
            join account in db.LinkedAccounts on ownership.LinkedAccountId equals account.Id
            where account.OwnerId == ownerId && ownership.UnlistedSince == null
            join entry in db.StoreEntries on ownership.StoreEntryId equals entry.Id
            join game in db.Games on entry.GameId equals game.Id
            join choice in db.CoverChoices.Where(c => c.OwnerId == ownerId) on game.Id equals choice.GameId into chosen
            from choice in chosen.DefaultIfEmpty()
            select new
            {
                GameId = game.Id,
                GameName = game.Name,
                StoreEntryId = entry.Id,
                LinkedAccountId = account.Id,
                entry.Source,
                entry.ExternalId,
                ownership.PlaytimeSeconds,
                ownership.RecentPlaytimeSeconds,
                ownership.LastPlayedAt,
                ownership.FirstSeenAt,
                ownership.AsOf,
                ownership.MissingSince,
                CoverSource = choice == null ? null : choice.Source,
            })
            .ToListAsync(ct);
        return [.. rows.Select(row => new OwnedEntry(
            row.GameId,
            row.GameName,
            row.StoreEntryId,
            row.LinkedAccountId,
            row.Source,
            row.ExternalId,
            Playtime(row.PlaytimeSeconds),
            Playtime(row.RecentPlaytimeSeconds),
            row.LastPlayedAt,
            row.FirstSeenAt,
            row.AsOf,
            row.MissingSince,
            row.CoverSource))];
    }

    public Task<string?> FindCoverChoiceAsync(Guid ownerId, Guid gameId, CancellationToken ct) =>
        db.CoverChoices.AsNoTracking()
            .Where(c => c.OwnerId == ownerId && c.GameId == gameId)
            .Select(c => c.Source)
            .FirstOrDefaultAsync(ct);

    public async Task SetCoverChoiceAsync(Guid ownerId, Guid gameId, string? source, DateTimeOffset at, CancellationToken ct)
    {
        if (source is null)
        {
            await db.CoverChoices.Where(c => c.OwnerId == ownerId && c.GameId == gameId).ExecuteDeleteAsync(ct);
            return;
        }

        // One statement: two choices made at once leave one row, the later one's.
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO games.cover_choices (owner_id, game_id, source, chosen_at) VALUES ({ownerId}, {gameId}, {source}, {at})
            ON CONFLICT (owner_id, game_id) DO UPDATE SET source = excluded.source, chosen_at = excluded.chosen_at
            """,
            ct);
    }

    public async Task<DateTimeOffset?> FindFirstSeenAsync(Guid ownerId, CancellationToken ct) =>
        await (
            from ownership in db.Ownerships.AsNoTracking()
            join account in db.LinkedAccounts on ownership.LinkedAccountId equals account.Id
            where account.OwnerId == ownerId && ownership.UnlistedSince == null
            select (DateTimeOffset?)ownership.FirstSeenAt)
            .MinAsync(ct);

    public async Task<IReadOnlyList<PlayedDay>> ListPlayedAsync(Guid ownerId, Guid? gameId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        // The gains were taken when the readings were written: what is left is to add up those of the
        // days asked, from the index on the account and the day (or, for one game, on its store
        // entries and the day), for the ownerships still in the library. A condition is added only
        // when it is asked for, so that each query is planned as it is written.
        var gains =
            from gain in db.PlaytimeGains.AsNoTracking()
            join account in db.LinkedAccounts on gain.LinkedAccountId equals account.Id
            where account.OwnerId == ownerId
            join ownership in db.Ownerships
                on new { gain.LinkedAccountId, gain.StoreEntryId } equals new { ownership.LinkedAccountId, ownership.StoreEntryId }
            where ownership.UnlistedSince == null
            join entry in db.StoreEntries on gain.StoreEntryId equals entry.Id
            select new { gain.Day, entry.GameId, gain.Seconds };
        if (gameId is { } game)
        {
            gains = gains.Where(gain => gain.GameId == game);
        }

        if (from is { } first)
        {
            gains = gains.Where(gain => gain.Day >= first);
        }

        if (to is { } last)
        {
            gains = gains.Where(gain => gain.Day <= last);
        }

        var rows = await gains
            .GroupBy(gain => new { gain.Day, gain.GameId })
            .Select(day => new { day.Key.Day, day.Key.GameId, Seconds = day.Sum(gain => gain.Seconds) })
            .ToListAsync(ct);
        return [.. rows.Select(row => new PlayedDay(row.Day, row.GameId, TimeSpan.FromSeconds(row.Seconds)))];
    }

    /// <summary>Runs a statement with PostgreSQL's just-in-time compilation off. The planner decides to
    /// compile from the cost it estimates, and it estimates the readings of <see cref="GainsSince"/> many
    /// times too high (rows read through the primary key are priced as if they were scattered, when a
    /// snapshot's rows are written together): the statement would be compiled at each reading, which
    /// takes about as long as running it. The setting is the transaction's own and ends with it.</summary>
    private async Task<T> WithoutJitAsync<T>(Func<Task<T>> run, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await db.Database.ExecuteSqlRawAsync(JitOff, ct);
            return await run();
        }

        // In a delegate for the same reason as EfUnitOfWork: the context retries on a lost connection.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync(JitOff, ct);
            var result = await run();
            await transaction.CommitAsync(ct);
            return result;
        });
    }

    private const string JitOff = "SET LOCAL jit = off";

    /// <summary>Deletes the gains of a linked account's readings from one of them on, before
    /// <see cref="GainsSince"/> writes them again: {0} is the account, {1} the reading.</summary>
    private const string ClearGainsSince =
        """
        DELETE FROM games.playtime_gains g
        USING games.raw_snapshots s, games.raw_snapshots since
        WHERE since.id = {1} AND since.linked_account_id = {0}
          AND g.snapshot_id = s.id
          AND s.linked_account_id = {0}
          AND (s.taken_at, s.id) >= (since.taken_at, since.id)
        """;

    /// <summary>Writes again the gains of a linked account's readings from one of them on: {0} is the
    /// account, {1} the reading, counted from with the readings after it in the order of their dates, then
    /// of their ids (two readings of the same moment come in the order they were recorded).
    ///
    /// For each store entry, the readings that knew a total (<c>known_playtime_seconds</c>) are taken in
    /// that order: a total greater than the one before is a gain, kept with the reading that shows it; a
    /// total that went down gives nothing, and neither does the first one, where the count starts. A
    /// reading that knew no total is skipped, so the next one is compared with the last that did.
    ///
    /// The first reading of each entry from {1} on is compared with the last one before {1} that knew a
    /// total, however far back: the snapshots are walked backwards from {1}, one row looked up in each,
    /// and the walk stops at the first that has one, or at the earliest reading that listed the entry: one
    /// bought in the reading {1} is looked up nowhere.
    ///
    /// A gain is of the day its game was last played, when the reading says that moment and it lies after
    /// the reading before and not after its own; otherwise of the day of its reading (UTC).
    ///
    /// The migration <c>PlaytimeGains</c> wrote the rows of the readings before it by the same rule: keep
    /// the two together. The gains are only written here and there: a statement that changes the
    /// observations (a migration that corrects them) writes them again too, or the history keeps the
    /// old differences.</summary>
    private const string GainsSince =
        """
        WITH since AS (
            SELECT taken_at, id FROM games.raw_snapshots WHERE id = {1} AND linked_account_id = {0}
        ),
        readings AS (
            SELECT s.id AS snapshot_id,
                   o.store_entry_id,
                   s.taken_at,
                   o.last_played_at,
                   o.known_playtime_seconds AS known,
                   LAG(o.known_playtime_seconds) OVER reading AS before,
                   LAG(s.taken_at) OVER reading AS before_at
            FROM since
            JOIN games.raw_snapshots s ON s.linked_account_id = {0} AND (s.taken_at, s.id) >= (since.taken_at, since.id)
            JOIN games.observations o ON o.snapshot_id = s.id
            WHERE o.known_playtime_seconds IS NOT NULL
            WINDOW reading AS (PARTITION BY o.store_entry_id ORDER BY s.taken_at, s.id)
        ),
        gains AS (
            SELECT r.snapshot_id, r.store_entry_id, r.taken_at, r.last_played_at, r.before_at, r.known - r.before AS gained
            FROM readings r
            WHERE r.before IS NOT NULL AND r.known > r.before
            UNION ALL
            SELECT r.snapshot_id, r.store_entry_id, r.taken_at, r.last_played_at, earlier.taken_at, r.known - earlier.known
            FROM readings r
            CROSS JOIN since
            JOIN games.ownerships w ON w.linked_account_id = {0} AND w.store_entry_id = r.store_entry_id
            CROSS JOIN LATERAL (
                SELECT p.known_playtime_seconds AS known, ps.taken_at
                FROM games.raw_snapshots ps
                JOIN games.observations p ON p.snapshot_id = ps.id
                WHERE ps.linked_account_id = {0}
                  AND ps.taken_at >= w.first_seen_at
                  AND (ps.taken_at, ps.id) < (since.taken_at, since.id)
                  AND p.store_entry_id = r.store_entry_id
                  AND p.known_playtime_seconds IS NOT NULL
                ORDER BY ps.taken_at DESC, ps.id DESC
                LIMIT 1) earlier
            WHERE r.before IS NULL
              AND r.known > earlier.known
        )
        INSERT INTO games.playtime_gains (snapshot_id, store_entry_id, linked_account_id, day, seconds)
        SELECT g.snapshot_id,
               g.store_entry_id,
               {0},
               (CASE WHEN g.last_played_at > g.before_at AND g.last_played_at <= g.taken_at
                     THEN g.last_played_at
                     ELSE g.taken_at
                END AT TIME ZONE 'UTC')::date,
               g.gained
        FROM gains g
        """;

    private static long? Seconds(TimeSpan? playtime) => playtime is { } value ? (long)value.TotalSeconds : null;

    private static TimeSpan? Playtime(long? seconds) => seconds is { } value ? TimeSpan.FromSeconds(value) : null;

}
