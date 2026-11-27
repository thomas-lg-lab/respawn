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

        var entries = await GameDataEntries(gameId).ToListAsync(ct);
        return new CatalogueGame(
            gameId,
            game.Name,
            [.. entries.Select(e => new GameStore(e.Id, e.Source, e.ExternalId))],
            First(entries, e => e.Url));
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
            g == null ? null : g.Url,
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
        string? Url,
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
        // The differences are taken by the database, which reads the observations of the days asked
        // and no others: their snapshots come from the index on the linked account and the date, their
        // rows from the primary key, snapshot by snapshot. What a month costs is then what a month
        // holds, however long the history is. A reading that knew no total is skipped, so the next one
        // is compared with the last that did. Two readings of the same moment come in the order they
        // were recorded (a snapshot's id is made from the time).
        //
        // Most readings repeat the total before them and are dropped as soon as the two are compared:
        // the game, the ownership and the day are looked up for the few that are left.
        //
        // The first reading of a game in the days asked counts from the last one before them that knew
        // a total, however far back: the snapshots are walked backwards from the first day, one row
        // looked up in each, and the walk stops at the first that has one, or at the earliest reading
        // that listed the game: one bought in the days asked is looked up nowhere. That lookup is made
        // for these first readings only, one per game, which the query says by keeping them apart.
        //
        // A gain is of the day its game was last played, when the reading says that moment and it
        // lies after the reading before; otherwise of the day of its reading. So the readings asked
        // are those of the days asked and the first one after them, which may hold the last evening
        // of those days, and the days asked are kept at the end.
        //
        // For one game, the readings start from its store entries: their rows come from the index on
        // the store entry, a row or two a day, and the rest of the library is not read. The game is
        // then a condition written in the query, not a value that switches it off: the plan is made
        // for the query as it is written, and one written for both would read the library each time.
        var since = (from ?? DateOnly.MinValue).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var until = to is { } day && day < DateOnly.MaxValue
            ? day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        var query = gameId is { } game
            ? db.Database.SqlQueryRaw<PlayedRow>(PlayedOfOneGame, ownerId, since, until, game)
            : db.Database.SqlQueryRaw<PlayedRow>(PlayedOfEveryGame, ownerId, since, until);
        var rows = await WithoutJitAsync(() => query.ToListAsync(ct), ct);
        return [.. rows.Select(row => new PlayedDay(row.Day, row.GameId, TimeSpan.FromSeconds(row.Seconds)))];
    }

    /// <summary>Runs a read with PostgreSQL's just-in-time compilation off. The planner decides to
    /// compile from the cost it estimates, and it estimates the readings of <see cref="ListPlayedAsync"/>
    /// many times too high (rows read through the primary key are priced as if they were scattered,
    /// when a snapshot's rows are written together): a few months of readings would be compiled at
    /// each call, which takes about as long as running them. The setting is the transaction's own and
    /// ends with it.</summary>
    private async Task<T> WithoutJitAsync<T>(Func<Task<T>> read, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await db.Database.ExecuteSqlRawAsync(JitOff, ct);
            return await read();
        }

        // In a delegate for the same reason as EfUnitOfWork: the context retries on a lost connection.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync(JitOff, ct);
            var result = await read();
            await transaction.CommitAsync(ct);
            return result;
        });
    }

    private const string JitOff = "SET LOCAL jit = off";

    /// <summary>The readings of every game over the days asked, each with the total before it: {0} is
    /// the owner, {1} and {2} the first instant asked and the first one after the last. The first
    /// reading at or after {2} is read too: what it found may have been played before. Every game
    /// has a known total in it once it had one (a reading that hides the playtime keeps the total
    /// known before), so no later reading can date a gain before {2}.
    ///
    /// The observations are asked snapshot by snapshot, and OFFSET 0 is what keeps it so: PostgreSQL
    /// does not merge a subquery that has one into the rest, and would otherwise read the whole table
    /// to keep a month of it, as long as the table is no more than a few years of readings.</summary>
    private const string ReadingsOfEveryGame =
        """
        WITH readings AS (
            SELECT s.linked_account_id,
                   o.store_entry_id,
                   s.taken_at,
                   o.last_played_at,
                   o.known,
                   LAG(o.known) OVER reading AS before,
                   LAG(s.taken_at) OVER reading AS before_at
            FROM games.linked_accounts a
            JOIN games.raw_snapshots s ON s.linked_account_id = a.id
            CROSS JOIN LATERAL (
                SELECT n.store_entry_id, n.known_playtime_seconds AS known, n.last_played_at
                FROM games.observations n
                WHERE n.snapshot_id = s.id
                  AND n.known_playtime_seconds IS NOT NULL
                OFFSET 0) o
            WHERE a.owner_id = {0}
              AND s.taken_at >= {1} AND s.taken_at <= COALESCE((
                  SELECT x.taken_at
                  FROM games.raw_snapshots x
                  WHERE x.linked_account_id = a.id AND x.taken_at >= {2}
                    AND EXISTS (SELECT 1 FROM games.observations n WHERE n.snapshot_id = x.id)
                  ORDER BY x.taken_at
                  LIMIT 1), {2})
            WINDOW reading AS (PARTITION BY s.linked_account_id, o.store_entry_id ORDER BY s.taken_at, s.id)
        ),

        """;

    /// <summary>The readings of one game, from its store entries: {3} is the game.</summary>
    private const string ReadingsOfOneGame =
        """
        WITH readings AS (
            SELECT s.linked_account_id,
                   o.store_entry_id,
                   s.taken_at,
                   o.last_played_at,
                   o.known_playtime_seconds AS known,
                   LAG(o.known_playtime_seconds) OVER reading AS before,
                   LAG(s.taken_at) OVER reading AS before_at
            FROM games.linked_accounts a
            JOIN games.raw_snapshots s ON s.linked_account_id = a.id
            JOIN games.observations o ON o.snapshot_id = s.id
            JOIN games.store_entries e ON e.id = o.store_entry_id
            WHERE a.owner_id = {0}
              AND s.taken_at >= {1} AND s.taken_at <= COALESCE((
                  SELECT x.taken_at
                  FROM games.raw_snapshots x
                  WHERE x.linked_account_id = a.id AND x.taken_at >= {2}
                    AND EXISTS (SELECT 1 FROM games.observations n WHERE n.snapshot_id = x.id)
                  ORDER BY x.taken_at
                  LIMIT 1), {2})
              AND o.known_playtime_seconds IS NOT NULL
              AND e.game_id = {3}
            WINDOW reading AS (PARTITION BY s.linked_account_id, o.store_entry_id ORDER BY s.taken_at, s.id)
        ),

        """;

    /// <summary>The rest of it: the readings that add to the one before, of the games still listed,
    /// then what each adds, dated, per day and per game, for the days asked. A first reading with
    /// nothing before the days asked adds nothing.</summary>
    private const string PlayedGains =
        """
        changes AS (
            SELECT r.linked_account_id,
                   r.store_entry_id,
                   e.game_id,
                   w.first_seen_at,
                   r.taken_at,
                   r.last_played_at,
                   r.known,
                   r.before,
                   r.before_at
            FROM readings r
            JOIN games.ownerships w ON w.linked_account_id = r.linked_account_id AND w.store_entry_id = r.store_entry_id
            JOIN games.store_entries e ON e.id = r.store_entry_id
            WHERE (r.before IS NULL OR r.known > r.before)
              AND w.unlisted_since IS NULL
        ),
        gains AS (
            SELECT c.taken_at, c.last_played_at, c.before_at, c.game_id, c.known - c.before AS gained
            FROM changes c
            WHERE c.before IS NOT NULL
            UNION ALL
            SELECT c.taken_at, c.last_played_at, earlier.taken_at, c.game_id, c.known - earlier.known
            FROM changes c
            CROSS JOIN LATERAL (
                SELECT p.known_playtime_seconds AS known, ps.taken_at
                FROM games.raw_snapshots ps
                JOIN games.observations p ON p.snapshot_id = ps.id
                WHERE ps.linked_account_id = c.linked_account_id
                  AND ps.taken_at >= c.first_seen_at AND ps.taken_at < {1}
                  AND p.store_entry_id = c.store_entry_id
                  AND p.known_playtime_seconds IS NOT NULL
                ORDER BY ps.taken_at DESC, ps.id DESC
                LIMIT 1) earlier
            WHERE c.before IS NULL
              AND c.known > earlier.known
        ),
        dated AS (
            SELECT (CASE WHEN g.last_played_at > g.before_at AND g.last_played_at <= g.taken_at
                         THEN g.last_played_at
                         ELSE g.taken_at
                    END AT TIME ZONE 'UTC')::date AS day,
                   g.game_id,
                   g.gained
            FROM gains g
        )
        SELECT d.day AS day, d.game_id AS game_id, SUM(d.gained)::bigint AS seconds
        FROM dated d
        WHERE d.day >= ({1} AT TIME ZONE 'UTC')::date AND d.day < ({2} AT TIME ZONE 'UTC')::date
        GROUP BY d.day, d.game_id
        """;

    private const string PlayedOfEveryGame = ReadingsOfEveryGame + PlayedGains;

    private const string PlayedOfOneGame = ReadingsOfOneGame + PlayedGains;

    private static long? Seconds(TimeSpan? playtime) => playtime is { } value ? (long)value.TotalSeconds : null;

    private static TimeSpan? Playtime(long? seconds) => seconds is { } value ? TimeSpan.FromSeconds(value) : null;

    /// <summary>A row of <see cref="ListPlayedAsync"/>, as the query names its columns.</summary>
    private sealed class PlayedRow
    {
        public DateOnly Day { get; set; }

        public Guid GameId { get; set; }

        public long Seconds { get; set; }
    }
}
