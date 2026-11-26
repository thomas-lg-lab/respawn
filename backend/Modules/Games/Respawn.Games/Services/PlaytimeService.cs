using Respawn.Games.Ports;

namespace Respawn.Games.Services;

/// <summary>The time played, read back: year by year, a game month by month, a month game by game.
/// Where a year is counted from is decided once, here (<see cref="CoverAsync"/>).</summary>
public sealed class PlaytimeService(
    ILinkedAccountStore linkedAccounts,
    ICatalogue catalogue,
    ILibraryStore library,
    IYearReviewStore reviews,
    TimeProvider clock) : IPlaytimeService
{
    public async Task<IReadOnlyList<PlayedYear>> GetYearsAsync(Guid ownerId, CancellationToken ct)
    {
        var cover = await CoverAsync(ownerId, ct);
        var (months, days) = await PlayedAsync(ownerId, gameId: null, cover, ct);
        return
        [
            .. cover.Select(year => new PlayedYear(
                year.Year,
                Sum(year.Source == YearSource.Review
                    ? months.Where(month => month.Month.Year == year.Year).Select(month => month.Playtime)
                    : days.Where(day => day.Date.Year == year.Year).Select(day => day.Playtime)),
                year.Source,
                year.From,
                year.To,
                year.Whole)),
        ];
    }

    public async Task<IReadOnlyList<GameMonth>> GetGameMonthsAsync(Guid ownerId, Guid gameId, CancellationToken ct)
    {
        if ((await catalogue.FindNamesAsync([gameId], ct)).Count == 0)
        {
            throw new GamesException(GamesError.UnknownGame);
        }

        var (months, days) = await PlayedAsync(ownerId, gameId, await CoverAsync(ownerId, ct), ct);
        return
        [
            .. months
                .GroupBy(month => month.Month)
                .Select(month => new GameMonth(month.Key, Sum(month.Select(account => account.Playtime)), YearSource.Review))
                .Concat(days
                    .GroupBy(day => new DateOnly(day.Date.Year, day.Date.Month, 1))
                    .Select(month => new GameMonth(month.Key, Sum(month.Select(day => day.Playtime)), YearSource.Readings)))
                // A review keeps a month a game was launched in and not played.
                .Where(month => month.Playtime > TimeSpan.Zero)
                .OrderBy(month => month.Month),
        ];
    }

    public async Task<LibraryMonth> GetMonthAsync(Guid ownerId, DateOnly month, CancellationToken ct)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var last = new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));
        var cover = (await CoverAsync(ownerId, ct)).FirstOrDefault(year => year.Year == first.Year);

        // The days of the month its year counts: all of them, or none, but for the year the readings
        // started in and the one that is not over.
        var from = cover is not null && cover.From > first ? cover.From : first;
        var to = cover is not null && cover.To < last ? cover.To : last;
        if (cover is null || from > to)
        {
            return new LibraryMonth(first, null, null, null, []);
        }

        IEnumerable<(Guid GameId, TimeSpan Playtime)> played = cover.Source == YearSource.Review
            ? (await reviews.ListPlayedAsync(ownerId, ct)).Where(item => item.Month == first).Select(item => (item.GameId, item.Playtime))
            : (await library.ListPlayedAsync(ownerId, gameId: null, from, to, ct)).Select(day => (day.GameId, day.Playtime));
        var totals = played
            .GroupBy(item => item.GameId)
            .Select(game => (GameId: game.Key, Playtime: Sum(game.Select(item => item.Playtime))))
            .Where(game => game.Playtime > TimeSpan.Zero)
            .ToList();

        // From the catalogue and not from the library: a review counts games that are no longer owned.
        var names = await catalogue.FindNamesAsync([.. totals.Select(game => game.GameId)], ct);
        return new LibraryMonth(
            first,
            cover.Source,
            from,
            to,
            [
                .. totals
                    .OrderByDescending(game => game.Playtime)
                    .ThenBy(game => game.GameId)
                    .Select(game => new MonthGame(game.GameId, names.GetValueOrDefault(game.GameId), game.Playtime)),
            ]);
    }

    /// <summary>The years an owner's time can be counted for, the earliest first; none while no store
    /// account is linked.
    ///
    /// A year the readings reach is the reviews' when every linked account has one of it: otherwise
    /// part of the time would be counted from a review and part from readings, which do not count the
    /// same games. A year before the first reading has the reviews only: it is counted from the
    /// accounts that have one, so that an account whose reviews cannot be read hides nothing of the
    /// others. The other years, from the first reading to today, are the readings'.</summary>
    private async Task<IReadOnlyList<YearCover>> CoverAsync(Guid ownerId, CancellationToken ct)
    {
        var accounts = await linkedAccounts.ListAsync(ownerId, ct);
        if (accounts.Count == 0)
        {
            return [];
        }

        var entries = await library.ListAsync(ownerId, ct);
        DateOnly? firstReading = entries.Count > 0 ? DateOnly.FromDateTime(entries.Min(entry => entry.FirstSeenAt).UtcDateTime) : null;
        var kept = (await reviews.ListOwnerYearsAsync(ownerId, ct)).ToLookup(pair => pair.Year, pair => pair.LinkedAccountId);
        var years = kept
            .Where(year => firstReading is not { } reached || year.Key < reached.Year || accounts.All(account => year.Contains(account.Id)))
            .ToDictionary(
                year => year.Key,
                year => new YearCover(year.Key, YearSource.Review, new DateOnly(year.Key, 1, 1), new DateOnly(year.Key, 12, 31), Whole: true));

        if (firstReading is { } since)
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            for (var year = since.Year; year <= today.Year; year++)
            {
                if (years.ContainsKey(year))
                {
                    continue;
                }

                var (first, last) = (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
                var from = since > first ? since : first;
                var to = today < last ? today : last;
                years[year] = new YearCover(year, YearSource.Readings, from, to, Whole: from == first && to == last);
            }
        }

        return [.. years.Values.OrderBy(year => year.Year)];
    }

    /// <summary>What was played over the years counted, of every game or of one: per month for the
    /// years of the reviews, per day for those of the readings, and nothing of a year under the other.
    /// For one game, only that game's readings are read: what it costs does not grow with the
    /// library.</summary>
    private async Task<(IReadOnlyList<PlayedMonth> Months, IReadOnlyList<PlayedDay> Days)> PlayedAsync(
        Guid ownerId, Guid? gameId, IReadOnlyList<YearCover> cover, CancellationToken ct)
    {
        var reviewed = cover.Where(year => year.Source == YearSource.Review).Select(year => year.Year).ToHashSet();
        var read = cover.Where(year => year.Source == YearSource.Readings).ToList();
        return (
            reviewed.Count > 0
                ? [.. (await reviews.ListPlayedAsync(ownerId, ct))
                    .Where(month => reviewed.Contains(month.Month.Year) && (gameId is null || month.GameId == gameId))]
                : [],
            read.Count > 0
                ? [.. (await library.ListPlayedAsync(ownerId, gameId, read.Min(year => year.From), read.Max(year => year.To), ct))
                    .Where(day => !reviewed.Contains(day.Date.Year))]
                : []);
    }

    private static TimeSpan Sum(IEnumerable<TimeSpan> times) => times.Aggregate(TimeSpan.Zero, (sum, time) => sum + time);

    /// <summary>A year something can be counted for, where from, and the days of it that are.</summary>
    private sealed record YearCover(int Year, YearSource Source, DateOnly From, DateOnly To, bool Whole);
}
