using Respawn.Games.Ports;

namespace Respawn.Games.Services;

/// <summary>The import of the stores' yearly reviews. A connector reads; what
/// a review becomes, and when one is asked for, is decided here.</summary>
public sealed class YearReviewService(
    IUnitOfWork unitOfWork,
    ILinkedAccountStore linkedAccounts,
    ICredentialStore credentials,
    ISnapshotStore snapshots,
    ICatalogue catalogue,
    IYearReviewStore reviews,
    ISyncRunStore runs,
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
        var accounts = await linkedAccounts.ListAllAsync(ct);
        var synced = await runs.FindLatestAsync([.. accounts.Select(account => account.Id)], ct);

        // Not before its first sync ended: that is what says whether the store shows this account.
        LinkedAccount[] askable =
        [
            .. accounts.Where(account => readable.Contains(account.Source)
                && synced.GetValueOrDefault(account.Id)?.Last is not null
                && !(gate.TriedAt(account.Id) > now - YearInReviewSchedule.RetryAfter)),
        ];
        if (askable.Length == 0)
        {
            return [];
        }

        // The years kept of all of them, asked once.
        var kept = await reviews.ListYearsAsync([.. askable.Select(account => account.Id)], ct);
        return
        [
            .. askable
                .Where(account => !kept.TryGetValue(account.Id, out var has) || years.Any(year => !has.Contains(year)))
                .OrderBy(account => account.LinkedAt)
                .ThenBy(account => account.Id)
                .Select(account => account.Id),
        ];
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
            var kept = (await reviews.ListYearsAsync([account.Id], ct)).GetValueOrDefault(account.Id);
            var imported = new List<int>();

            string? credential;
            try
            {
                credential = await credentials.FindAsync(account.Id, ct);
            }
            catch (CredentialException ex)
            {
                return new YearImport(imported, SourceError.NotConfigured, "the key given on this account cannot be read: " + ex.Message);
            }

            // A store that shows nothing of an account answers no review either, as it answers no
            // library: its word that a year has none is taken only when the last sync, made with the
            // same access, read a library.
            var shown = await runs.FindLatestAsync(account.Id, ct) is { Outcome: SyncOutcome.Succeeded } last
                && last.OwnCredential == credential is not null;

            foreach (var year in YearInReviewSchedule.Years(now).Where(year => kept?.Contains(year) != true))
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
                    return new YearImport(imported, ex.Error, ex.Message);
                }
                catch (ArgumentException ex)
                {
                    return new YearImport(imported, SourceError.Unreadable, "the connector does not take this account: " + ex.Message);
                }

                if (!reading.Exists && (!shown || now < YearInReviewSchedule.Settled(year)))
                {
                    // Asked again at the next attempt.
                    continue;
                }

                // A review, or none of a settled year of an account the store shows: nothing was
                // played that year on this account, which the other accounts of the owner must not
                // wait for.

                try
                {
                    await RecordAsync(account, year, reading, ct);
                }
                catch (ArgumentException ex)
                {
                    return new YearImport(imported, SourceError.Unreadable, "the review could not be recorded: " + ex.Message);
                }

                imported.Add(year);
            }

            return new YearImport(imported);
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

                var (_, entries) = await catalogue.EnsureAsync(
                    account.Source,
                    [.. months.Select(month => month.ExternalId).Distinct(StringComparer.Ordinal).Select(id => (id, (string?)null))],
                    now,
                    ct);

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
