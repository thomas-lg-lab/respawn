using Respawn.Games.Ports;

namespace Respawn.Games.Services;

/// <summary>The import of the stores' yearly reviews, and the years of an owner. A connector reads; what
/// a review becomes, and when one is asked for, is decided here.</summary>
public sealed class YearReviewService(
    IUnitOfWork unitOfWork,
    ILinkedAccountStore linkedAccounts,
    ICredentialStore credentials,
    ISnapshotStore snapshots,
    ICatalogue catalogue,
    IYearReviewStore reviews,
    ILibraryStore library,
    IEnumerable<IYearReviewSource> sources,
    YearInReviewGate gate,
    TimeProvider clock) : IYearReviewService
{
    public async Task<IReadOnlyList<Guid>> GetDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        int[] years = [.. YearInReviewSchedule.Years(now)];
        if (years.Length == 0)
        {
            return [];
        }

        var readable = sources.Select(source => source.Source).ToHashSet(StringComparer.Ordinal);
        var due = new List<LinkedAccount>();
        foreach (var account in await linkedAccounts.ListAllAsync(ct))
        {
            if (!readable.Contains(account.Source) || gate.TriedAt(account.Id) > now - YearInReviewSchedule.RetryAfter)
            {
                continue;
            }

            var kept = await reviews.ListYearsAsync(account.Id, ct);
            if (years.Any(year => !kept.Contains(year)))
            {
                due.Add(account);
            }
        }

        return [.. due.OrderBy(account => account.LinkedAt).ThenBy(account => account.Id).Select(account => account.Id)];
    }

    public async Task<YearImport?> ImportAsync(Guid linkedAccountId, CancellationToken ct)
    {
        if (await linkedAccounts.FindByIdAsync(linkedAccountId, ct) is not { } account
            || sources.FirstOrDefault(source => source.Source == account.Source) is not { } source
            || !gate.TryEnter(account.Id))
        {
            return null;
        }

        try
        {
            var now = clock.GetUtcNow();
            if (gate.TriedAt(account.Id) > now - YearInReviewSchedule.RetryAfter)
            {
                return null;
            }

            gate.Tried(account.Id, now);
            var kept = await reviews.ListYearsAsync(account.Id, ct);
            var imported = new List<int>();
            var notThere = new List<int>();

            string? credential;
            try
            {
                credential = await credentials.FindAsync(account.Id, ct);
            }
            catch (CredentialException ex)
            {
                return new YearImport(imported, notThere, SourceError.NotConfigured, "the key given on this account cannot be read: " + ex.Message);
            }

            foreach (var year in YearInReviewSchedule.Years(now).Where(year => !kept.Contains(year)))
            {
                YearReading reading;
                try
                {
                    reading = await source.ReadYearAsync(account.ExternalId, year, credential, ct);
                }
                catch (SourceException ex)
                {
                    // The years after it wait for the next attempt: a store that refused or did not
                    // answer for one year would most likely say the same for the next.
                    return new YearImport(imported, notThere, ex.Error, ex.Message);
                }
                catch (ArgumentException ex)
                {
                    return new YearImport(imported, notThere, SourceError.Unreadable, "the connector does not take this account: " + ex.Message);
                }

                if (!reading.Exists && now < YearInReviewSchedule.Settled(year))
                {
                    notThere.Add(year);
                    continue;
                }

                // A review, or none of a year that is settled: nothing was played that year on this
                // account, which the other accounts of the owner must not wait for.

                try
                {
                    await RecordAsync(account, year, reading, ct);
                }
                catch (ArgumentException ex)
                {
                    return new YearImport(imported, notThere, SourceError.Unreadable, "the review could not be recorded: " + ex.Message);
                }

                imported.Add(year);
            }

            return new YearImport(imported, notThere);
        }
        catch (GamesException ex) when (ex.Error == GamesError.UnknownLinkedAccount)
        {
            // Unlinked while it was read: nothing of it is kept.
            return null;
        }
        finally
        {
            gate.Leave(account.Id);
        }
    }

    public async Task<IReadOnlyList<PlayedYear>> GetYearsAsync(Guid ownerId, CancellationToken ct)
    {
        var accounts = await linkedAccounts.ListAsync(ownerId, ct);
        if (accounts.Count == 0)
        {
            return [];
        }

        // A year is the reviews' when every linked account has one of it: otherwise part of the time
        // would be counted from a review and part from readings, which do not count the same games.
        var kept = (await reviews.ListOwnerYearsAsync(ownerId, ct)).ToLookup(pair => pair.Year, pair => pair.LinkedAccountId);
        var reviewed = kept
            .Where(year => accounts.All(account => year.Contains(account.Id)))
            .Select(year => year.Key)
            .ToHashSet();

        var years = new Dictionary<int, PlayedYear>();
        if (reviewed.Count > 0)
        {
            var months = (await reviews.ListPlayedAsync(ownerId, ct))
                .Where(month => reviewed.Contains(month.Month.Year))
                .ToLookup(month => month.Month.Year, month => month.Playtime);
            foreach (var year in reviewed)
            {
                years[year] = new PlayedYear(
                    year,
                    months[year].Aggregate(TimeSpan.Zero, (sum, time) => sum + time),
                    YearSource.Review,
                    new DateOnly(year, 1, 1),
                    new DateOnly(year, 12, 31),
                    Whole: true);
            }
        }

        // The readings, from the first one to today, for the years no review covers.
        var entries = await library.ListAsync(ownerId, ct);
        if (entries.Count > 0)
        {
            var since = DateOnly.FromDateTime(entries.Min(entry => entry.FirstSeenAt).UtcDateTime);
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var played = (await library.ListPlayedAsync(ownerId, since, today, ct))
                .ToLookup(day => day.Date.Year, day => day.Playtime);
            for (var year = since.Year; year <= today.Year; year++)
            {
                if (years.ContainsKey(year))
                {
                    continue;
                }

                var (first, last) = (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
                var from = since > first ? since : first;
                var to = today < last ? today : last;
                years[year] = new PlayedYear(
                    year,
                    played[year].Aggregate(TimeSpan.Zero, (sum, time) => sum + time),
                    YearSource.Readings,
                    from,
                    to,
                    Whole: from == first && to == last);
            }
        }

        return [.. years.Values.OrderBy(year => year.Year)];
    }

    /// <summary>One year's review, kept with its answer in one transaction (an answer with no review
    /// is kept the same way, with no month). A game the catalogue does not know gets a record of its
    /// own, with no name: a review names no game, and the next reading of the library names those still
    /// owned.</summary>
    private Task RecordAsync(LinkedAccount account, int year, YearReading reading, CancellationToken ct)
    {
        var months = Months(year, reading);
        var takenAt = reading.TakenAt.ToUniversalTime();
        return unitOfWork.RunAsync(
            async () =>
            {
                // Under the lock unlinking takes: the account is there, or nothing is kept.
                if (await linkedAccounts.FindByIdAsync(account.Id, ct) is null)
                {
                    throw new GamesException(GamesError.UnknownLinkedAccount);
                }

                var now = clock.GetUtcNow();
                var snapshot = new RawSnapshot(Guid.CreateVersion7(), account.Source, reading.Kind, takenAt, now, account.Id, reading.Payload, Complete: true);
                await snapshots.AddAsync(snapshot, ct);

                string[] ids = [.. months.Select(month => month.ExternalId).Distinct(StringComparer.Ordinal)];
                var known = await catalogue.FindStoreEntriesAsync(account.Source, ids, ct);
                NewStoreEntry[] unknown =
                [
                    .. ids.Where(id => !known.ContainsKey(id))
                        .Select(id => new NewStoreEntry(Guid.CreateVersion7(), Guid.CreateVersion7(), account.Source, id, Name: null)),
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

                await reviews.RecordAsync(
                    account.Id,
                    year,
                    snapshot.Id,
                    [.. months.Select(month => new MonthPlayed(entries[month.ExternalId], month.Month, month.Playtime, month.Sessions))],
                    ct);
                return true;
            },
            ct);
    }

    /// <summary>The months of a review, checked: all in the year reviewed, nothing negative, and one item
    /// per game and month (two are added up).</summary>
    private static List<MonthItem> Months(int year, YearReading reading)
    {
        if (reading.Year != year || reading.Payload is null || reading.Months is null)
        {
            throw new ArgumentException($"expected a review of {year}", nameof(reading));
        }

        if (reading.TakenAt.ToUniversalTime().Year <= year)
        {
            // A review of a year is made once it is over: one dated inside it is not a whole year.
            throw new ArgumentException($"the review of {year} is dated before the year ended", nameof(reading));
        }

        var months = new Dictionary<(string, DateOnly), MonthItem>();
        foreach (var item in reading.Months)
        {
            if (string.IsNullOrWhiteSpace(item.ExternalId) || item.ExternalId.Length > Limits.ExternalId)
            {
                throw new ArgumentException($"expected a game id of 1 to {Limits.ExternalId} characters", nameof(reading));
            }

            if (item.Month.Year != year || item.Month.Day != 1)
            {
                throw new ArgumentException($"the month {item.Month:yyyy-MM-dd} is not a month of {year}", nameof(reading));
            }

            if (item.Playtime < TimeSpan.Zero || item.Sessions < 0)
            {
                throw new ArgumentException($"the game {item.ExternalId} has a negative playtime or session count", nameof(reading));
            }

            var key = (item.ExternalId, item.Month);
            months[key] = months.TryGetValue(key, out var before)
                ? before with { Playtime = before.Playtime + item.Playtime, Sessions = before.Sessions + item.Sessions }
                : item;
        }

        return [.. months.Values.Where(month => month.Playtime > TimeSpan.Zero || month.Sessions > 0)];
    }
}
