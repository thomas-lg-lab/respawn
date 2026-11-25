using System.Globalization;
using System.Text.Json;
using Respawn.Games.Ports;

namespace Respawn.Games.Steam;

/// <summary>
/// Steam's Year in Review of an account: what it played per month and per game, read through
/// <c>ISaleFeatureService/GetUserYearInReview</c> with the same key as the library. The method is not in
/// Valve's documentation; its shape is in the protobufs the community extracts from the Steam client
/// (<c>webui/service_salefeature.proto</c>). It answers from 2022 on, and for a year once Steam has made
/// its review, around its end; before that, an empty <c>response</c>.
///
/// The answer is handed over untouched, beside its months. Steam counts the months' playtime in seconds,
/// and dates a month by its start in seconds since 1970.
/// </summary>
internal sealed class SteamYearReviewSource(SteamClient steam) : IYearReviewSource
{
    /// <summary>Which of Steam's answers a reading is: the name its snapshots are kept under.</summary>
    public const string Kind = "year-in-review";

    public string Source => "steam";

    public async Task<YearReading> ReadYearAsync(string externalId, int year, string? credential, CancellationToken ct)
    {
        SteamClient.CheckSteamId(externalId);
        ArgumentOutOfRangeException.ThrowIfLessThan(year, YearInReviewSchedule.FirstYear);

        var (body, takenAt) = await steam.GetAsync(
            string.Create(CultureInfo.InvariantCulture, $"ISaleFeatureService/GetUserYearInReview/v1/?steamid={externalId}&year={year}&format=json"),
            credential,
            ct);

        var (exists, months) = Months(body, year);
        return new YearReading(year, Kind, takenAt, body, exists, months);
    }

    /// <summary>Whether the answer holds a review of the year, and its months.</summary>
    private static (bool Exists, IReadOnlyList<MonthItem> Months) Months(string body, int year)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("response", out var response)
                || response.ValueKind != JsonValueKind.Object)
            {
                throw new SourceException(SourceError.Unreadable, "Steam's answer has no response");
            }

            // No review of that year yet: an empty response.
            if (!response.TryGetProperty("stats", out var stats)
                || !stats.TryGetProperty("playtime_stats", out var playtime))
            {
                return (false, []);
            }

            if (stats.TryGetProperty("year", out var said) && Number(said) != year)
            {
                throw new SourceException(SourceError.Unreadable, $"Steam answered the review of another year than {year}");
            }

            // Every field of Steam's messages is optional, and an empty one is left out of the answer:
            // what cannot be placed (a month with no date, a game with no id or no figures) is skipped,
            // rather than costing the whole year.
            var items = new List<MonthItem>();
            if (playtime.TryGetProperty("months", out var months))
            {
                foreach (var month in months.EnumerateArray())
                {
                    if (!month.TryGetProperty("rtime_month", out var dated) || !month.TryGetProperty("appid", out var games))
                    {
                        continue;
                    }

                    var start = Month(Number(dated));
                    if (start.Year != year)
                    {
                        throw new SourceException(SourceError.Unreadable, $"Steam's review of {year} holds a month of {start.Year}");
                    }

                    foreach (var game in games.EnumerateArray())
                    {
                        if (!game.TryGetProperty("appid", out var appId) || !game.TryGetProperty("stats", out var figures))
                        {
                            continue;
                        }

                        items.Add(new MonthItem(
                            Number(appId).ToString(CultureInfo.InvariantCulture),
                            start,
                            TimeSpan.FromSeconds(figures.TryGetProperty("total_playtime_seconds", out var seconds) ? Number(seconds) : 0),
                            figures.TryGetProperty("total_sessions", out var sessions) ? (int)Math.Min(Number(sessions), int.MaxValue) : 0));
                    }
                }
            }

            // A review that says time was played and gives no month of it is not a year with nothing
            // played: a review kept private, or not finished, may look like that. Refused, and asked
            // again the next day, rather than kept as zero for good.
            if (items.Count == 0
                && playtime.TryGetProperty("total_stats", out var total)
                && total.TryGetProperty("total_playtime_seconds", out var played)
                && Number(played) > 0)
            {
                throw new SourceException(SourceError.Unreadable, $"Steam's review of {year} gives a total and no month");
            }

            return (true, items);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or ArgumentOutOfRangeException or OverflowException)
        {
            throw new SourceException(SourceError.Unreadable, "Steam's review could not be read", ex);
        }
    }

    /// <summary>The month a review dates by its start. Steam's start of a month is not midnight UTC (its
    /// own time zone, which the answer does not say): a day and a half after it is that month whatever
    /// the zone.</summary>
    private static DateOnly Month(uint start)
    {
        var day = DateTimeOffset.FromUnixTimeSeconds(start).AddHours(36).UtcDateTime;
        return new DateOnly(day.Year, day.Month, 1);
    }

    /// <summary>A whole number of the answer: Steam writes its 32-bit numbers as numbers, and some of its
    /// answers write them as strings.</summary>
    private static uint Number(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? uint.Parse(value.GetString()!, NumberStyles.None, CultureInfo.InvariantCulture)
            : value.GetUInt32();
}
