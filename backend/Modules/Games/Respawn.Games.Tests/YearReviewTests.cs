using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Respawn.Games.Api;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Games.Ports;
using Respawn.Games.Steam;
using Respawn.Games.Tests.Support;
using Respawn.Testing;

namespace Respawn.Games.Tests;

/// <summary>The import of the past from Steam's Year in Review, and the years it lets an owner see. Steam
/// answers from memory, and the clock is the test's: a turn of the import is run by hand, as the app's
/// timer would.</summary>
public sealed class YearReviewTests
{
    private const string SteamId = "76561190000000001";

    /// <summary>A moment of 2026: the reviews asked for are those of 2022 to 2025.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_connector_reads_the_months_of_a_recorded_review()
    {
        var steam = new FakeSteam { Body = FakeSteam.Recorded("year-in-review-2024.json") };
        await using var app = await StartAsync(steam, new TestClock { Now = Now });

        var reading = await Source(app).ReadYearAsync(SteamId, 2024, credential: null, default);

        Assert.True(reading.Exists);
        Assert.Equal((2024, "year-in-review"), (reading.Year, reading.Kind));
        Assert.Equal(FakeSteam.Recorded("year-in-review-2024.json"), reading.Payload);
        Assert.Equal(
            [
                ("620", new DateOnly(2024, 1, 1), 5400L, 3),
                ("220", new DateOnly(2024, 1, 1), 3600L, 2),
                ("620", new DateOnly(2024, 3, 1), 1800L, 1),
                ("70", new DateOnly(2024, 3, 1), 900L, 1),
            ],
            reading.Months.Select(month => (month.ExternalId, month.Month, (long)month.Playtime.TotalSeconds, month.Sessions)).ToArray());

        // The method, the account and the year in the address, and the key in a header only.
        var (address, key) = Assert.Single(steam.Requests);
        Assert.Equal("/ISaleFeatureService/GetUserYearInReview/v1/", address.AbsolutePath);
        Assert.Equal($"?steamid={SteamId}&year=2024&format=json", address.Query);
        Assert.Equal(FakeSteam.Key, key);

        // A year Steam has not reviewed: an empty response, which is no failure.
        steam.Body = FakeSteam.Recorded("year-in-review-none.json");
        var none = await Source(app).ReadYearAsync(SteamId, 2026, credential: null, default);
        Assert.False(none.Exists);
        Assert.Empty(none.Months);
    }

    [Fact]
    public async Task An_answer_for_another_year_or_with_a_month_of_another_year_is_unreadable()
    {
        var steam = new FakeSteam { Body = FakeSteam.Recorded("year-in-review-2024.json") };
        await using var app = await StartAsync(steam, new TestClock { Now = Now });

        var other = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadYearAsync(SteamId, 2023, null, default));
        Assert.Equal(SourceError.Unreadable, other.Error);

        steam.Body = Review(2024, (new DateOnly(2025, 1, 1), 620, 600, 1));
        var stray = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadYearAsync(SteamId, 2024, null, default));
        Assert.Equal(SourceError.Unreadable, stray.Error);

        steam.Body = "{\"response\":{\"stats\":{\"year\":2024,\"playtime_stats\":{\"months\":[{\"rtime_month\":\"x\"}]}}}}";
        Assert.Equal(SourceError.Unreadable, (await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadYearAsync(SteamId, 2024, null, default))).Error);

        // Not a SteamID: nothing is asked.
        var before = steam.Requests.Count;
        await Assert.ThrowsAsync<ArgumentException>(() => Source(app).ReadYearAsync("not-a-steam-id", 2024, null, default));
        Assert.Equal(before, steam.Requests.Count);
    }

    [Fact]
    public async Task A_linked_account_gets_the_years_steam_reviewed_and_the_others_on_a_later_day()
    {
        var clock = new TestClock { Now = Now };
        var reviews = new Dictionary<int, string> { [2024] = FakeSteam.Recorded("year-in-review-2024.json") };
        var steam = new FakeSteam { BodyFor = address => ReviewFor(address, reviews) };
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        // Linked a moment ago: due at once.
        Assert.Equal([account], await DueAsync(app));
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(
            ["2022", "2023", "2024", "2025"],
            steam.Requests.Select(request => Year(request.Address)).ToArray());

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            var review = Assert.Single(await db.YearReviews.AsNoTracking().ToListAsync());
            Assert.Equal((account, 2024), (review.LinkedAccountId, review.Year));

            // The answer kept whole, as a snapshot of the account.
            var snapshot = await db.RawSnapshots.AsNoTracking().SingleAsync(s => s.Id == review.SnapshotId);
            Assert.Equal(("steam", "year-in-review", account, true), (snapshot.Source, snapshot.Kind, snapshot.LinkedAccountId!.Value, snapshot.Complete));
            Assert.Equal(reviews[2024], snapshot.Payload);

            // Its months, by store entry; the games the catalogue did not know are in it now, unnamed.
            var entries = await db.StoreEntries.AsNoTracking().ToDictionaryAsync(e => e.Id);
            var months = await db.MonthlyPlaytime.AsNoTracking().ToListAsync();
            Assert.Equal(
                [("220", "2024-01", 3600L, 2), ("620", "2024-01", 5400L, 3), ("620", "2024-03", 1800L, 1), ("70", "2024-03", 900L, 1)],
                months
                    .Select(month => (entries[month.StoreEntryId].ExternalId, month.Month.ToString("yyyy-MM"), month.PlaytimeSeconds, month.Sessions))
                    .OrderBy(month => month.Item2).ThenBy(month => month.ExternalId, StringComparer.Ordinal)
                    .ToArray());
            Assert.All(months, month => Assert.Equal(snapshot.Id, month.SnapshotId));
            Assert.All(entries.Values, entry => Assert.Null(entry.Name));

            // No library comes of it: a review says what was played, not what is owned.
            Assert.Empty(await db.Ownerships.AsNoTracking().ToListAsync());
        }

        // Tried today: the years still missing wait for the next day.
        clock.Advance(TimeSpan.FromHours(20));
        Assert.Empty(await DueAsync(app));
        Assert.Equal(0, await TurnAsync(app));

        // The next day Steam has made the review of 2025: only the years missing are asked.
        reviews[2025] = Review(2025, (new DateOnly(2025, 6, 1), 620, 7200, 2));
        clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal([account], await DueAsync(app));
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(
            ["2022", "2023", "2024", "2025", "2022", "2023", "2025"],
            steam.Requests.Select(request => Year(request.Address)).ToArray());
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            Assert.Equal([2024, 2025], await db.YearReviews.AsNoTracking().Select(r => r.Year).OrderBy(year => year).ToListAsync());
        }
    }

    [Fact]
    public async Task A_review_that_cannot_be_read_stops_the_import_and_leaves_the_sync_alone()
    {
        var clock = new TestClock { Now = Now };
        var library = "{\"response\":{\"game_count\":1,\"games\":[{\"appid\":620,\"name\":\"Portal 2\",\"playtime_forever\":90}]}}";
        var refused = true;
        var steam = new FakeSteam
        {
            BodyFor = address => address.AbsolutePath.Contains("GetOwnedGames", StringComparison.Ordinal) ? library : null,
        };
        steam.Before = () =>
        {
            steam.Status = refused && steam.Requests[^1].Address.AbsolutePath.Contains("YearInReview", StringComparison.Ordinal)
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK;
            return Task.CompletedTask;
        };
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        // The first year fails: the next ones are not asked, and nothing is kept.
        YearImport? import;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            import = await scope.ServiceProvider.GetRequiredService<IYearReviewService>().ImportAsync(account, default);
        }

        Assert.NotNull(import);
        Assert.Equal((SourceError.Unavailable, 0), (import.Error, import.Imported.Count));
        Assert.Single(steam.Requests);

        // The sync of the day goes on as if nothing happened.
        Assert.Equal(1, await SyncTurnAsync(app));
        var game = Assert.Single((await browser.GetAsync("/api/v1/library").JsonAsync()).GetProperty("games").EnumerateArray());
        Assert.Equal("Portal 2", game.GetProperty("name").GetString());

        // Not asked again the same day; the next day, Steam answers.
        Assert.Empty(await DueAsync(app));
        refused = false;
        steam.BodyFor = address => address.AbsolutePath.Contains("GetOwnedGames", StringComparison.Ordinal)
            ? library
            : Year(address) == "2023" ? Review(2023, (new DateOnly(2023, 2, 1), 620, 600, 1)) : FakeSteam.Recorded("year-in-review-none.json");
        clock.NextDay();
        Assert.Equal(1, await TurnAsync(app));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            Assert.Equal([2023], await db.YearReviews.AsNoTracking().Select(r => r.Year).ToListAsync());
        }
    }

    [Fact]
    public async Task The_years_come_from_the_reviews_then_from_the_readings()
    {
        var clock = new TestClock { Now = Now };
        var steam = new FakeSteam
        {
            BodyFor = address => ReviewFor(address, new() { [2024] = FakeSteam.Recorded("year-in-review-2024.json"), [2025] = Review(2025) }),
        };
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await browser.GetAsync("/api/v1/library/years").RefusalAsync());

        var account = await LinkAsync(app, browser);
        Assert.Empty(await YearsAsync(browser));

        // Two readings in November 2026: an hour of Portal 2 between them.
        await RecordAsync(app, account, new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero), 90);
        await RecordAsync(app, account, new DateTimeOffset(2026, 11, 2, 6, 0, 0, TimeSpan.Zero), 150);
        Assert.Equal(
            [(2026, 3600L, "readings", "2026-11-01", "2026-11-28", false)],
            await YearsAsync(browser));

        // The reviews: 2024 with its 3 h 15 min, Half-Life counted though the library does not own it, and
        // 2025 reviewed with nothing played. 2022 and 2023 were not reviewed: they are not there.
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(
            [
                (2024, 11700L, "review", "2024-01-01", "2024-12-31", true),
                (2025, 0L, "review", "2025-01-01", "2025-12-31", true),
                (2026, 3600L, "readings", "2026-11-01", "2026-11-28", false),
            ],
            await YearsAsync(browser));

        // Another owner sees nothing of it.
        TestDatabase.Run("INSERT INTO accounts.settings (key, value) VALUES ('registration_open', 'true') ON CONFLICT DO NOTHING");
        using var other = app.Browser();
        await other.RegisterAsync("second@example.org");
        Assert.Empty(await YearsAsync(other));
    }

    [Fact]
    public async Task A_year_is_the_reviews_only_when_every_linked_account_has_one()
    {
        var clock = new TestClock { Now = Now };
        var steam = new FakeSteam { BodyFor = address => ReviewFor(address, new() { [2024] = FakeSteam.Recorded("year-in-review-2024.json") }) };
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var first = await LinkAsync(app, browser);
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal([(2024, 11700L, "review", "2024-01-01", "2024-12-31", true)], await YearsAsync(browser));

        // A second Steam account, not imported yet: 2024 is no longer whole, and no reading covers it.
        var owner = await OwnerAsync(app, first);
        await UseAsync(app, library => library.LinkAccountAsync(owner, "steam", "76561190000000002", null, default));
        Assert.Empty(await YearsAsync(browser));

        // Once it is, the two reviews add up.
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal([(2024, 23400L, "review", "2024-01-01", "2024-12-31", true)], await YearsAsync(browser));
    }

    [Fact]
    public async Task Unlinking_forgets_the_reviews()
    {
        var steam = new FakeSteam { BodyFor = address => ReviewFor(address, new() { [2024] = FakeSteam.Recorded("year-in-review-2024.json") }) };
        await using var app = await StartAsync(steam, new TestClock { Now = Now });
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        Assert.Equal(1, await TurnAsync(app));

        await UseAsync(app, async library =>
        {
            await library.UnlinkAccountAsync(await OwnerAsync(app, account), account, default);
            return true;
        });

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Empty(await db.YearReviews.AsNoTracking().ToListAsync());
        Assert.Empty(await db.MonthlyPlaytime.AsNoTracking().ToListAsync());
        Assert.Empty(await db.RawSnapshots.AsNoTracking().ToListAsync());
        Assert.Empty(await YearsAsync(browser));
    }

    [Fact]
    public async Task The_description_says_what_the_years_answer()
    {
        await using var app = await StartAsync(new FakeSteam(), new TestClock { Now = Now });
        using var browser = app.Browser();
        var document = await browser.GetAsync("/api/v1/openapi.json").JsonAsync();

        var route = document.GetProperty("paths").GetProperty("/api/v1/library/years").GetProperty("get");
        Assert.Equal(["200", "401"], route.GetProperty("responses").EnumerateObject().Select(response => response.Name).Order());
        var schemas = document.GetProperty("components").GetProperty("schemas");
        string[] Fields(string schema) =>
            [.. schemas.GetProperty(schema).GetProperty("properties").EnumerateObject().Select(field => field.Name)];
        Assert.Equal(["years"], Fields("LibraryYearsResponse"));
        Assert.Equal(["year", "seconds", "source", "first_day", "last_day", "whole"], Fields("YearResponse"));
        Assert.Equal("date", schemas.GetProperty("YearResponse").GetProperty("properties").GetProperty("first_day").GetProperty("format").GetString());
    }

    /// <summary>Steam's review of a year, in Steam's shape: each item a month, an app id, seconds and
    /// sessions. With none, a year reviewed with nothing played.</summary>
    private static string Review(int year, params (DateOnly Month, int AppId, int Seconds, int Sessions)[] items) =>
        JsonSerializer.Serialize(new
        {
            response = new
            {
                stats = new
                {
                    account_id = 1,
                    year,
                    playtime_stats = new
                    {
                        months = items.GroupBy(item => item.Month).Select(month => new
                        {
                            // Midnight in Steam's Pacific time.
                            rtime_month = new DateTimeOffset(month.Key.Year, month.Key.Month, 1, 8, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
                            appid = month.Select(item => new
                            {
                                appid = item.AppId,
                                stats = new { total_playtime_seconds = item.Seconds, total_sessions = item.Sessions },
                            }),
                        }),
                    },
                },
            },
        });

    /// <summary>Steam answering a review from those given, by year, and nothing for the others.</summary>
    private static string? ReviewFor(Uri address, Dictionary<int, string> reviews) =>
        address.AbsolutePath.Contains("GetUserYearInReview", StringComparison.Ordinal)
            ? reviews.GetValueOrDefault(int.Parse(Year(address), System.Globalization.CultureInfo.InvariantCulture))
                ?? FakeSteam.Recorded("year-in-review-none.json")
            : null;

    private static string Year(Uri address) =>
        address.Query.TrimStart('?').Split('&').Single(part => part.StartsWith("year=", StringComparison.Ordinal))["year=".Length..];

    private static Task<TestApp> StartAsync(FakeSteam steam, TestClock clock) =>
        steam.StartAppAsync(services: services => services.AddSingleton<TimeProvider>(clock));

    private static IYearReviewSource Source(TestApp app) => app.Services.GetRequiredService<IYearReviewSource>();

    private static async Task<Guid> LinkAsync(TestApp app, HttpClient browser)
    {
        var owner = (await browser.RegisterAsync().JsonAsync()).GetProperty("id").GetGuid();
        return (await UseAsync(app, library => library.LinkAccountAsync(owner, "steam", SteamId, null, default))).Id;
    }

    private static async Task<Guid> OwnerAsync(TestApp app, Guid linkedAccount)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        return (await db.LinkedAccounts.AsNoTracking().SingleAsync(a => a.Id == linkedAccount)).OwnerId;
    }

    /// <summary>A reading of the library, as a sync would record it: Portal 2 and its total in minutes.</summary>
    private static Task<Guid> RecordAsync(TestApp app, Guid account, DateTimeOffset takenAt, int minutes) =>
        UseAsync(app, library => library.RecordLibraryAsync(
            account,
            new LibraryReading("owned-games", takenAt, "{}", [new OwnedItem("620", "Portal 2", TimeSpan.FromMinutes(minutes), null)]),
            default));

    private static async Task<T> UseAsync<T>(TestApp app, Func<ILibraryService, Task<T>> use)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<ILibraryService>());
    }

    /// <summary>One turn of the import, as the app's timer runs it. Returns how many imports ended.</summary>
    private static Task<int> TurnAsync(TestApp app) =>
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<YearReviewScheduler>()).RunDueAsync(default);

    private static Task<int> SyncTurnAsync(TestApp app) =>
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<SyncScheduler>()).RunDueAsync(default);

    private static async Task<IReadOnlyList<Guid>> DueAsync(TestApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IYearReviewService>().GetDueAsync(default);
    }

    private static async Task<(int Year, long Seconds, string Source, string FirstDay, string LastDay, bool Whole)[]> YearsAsync(HttpClient browser) =>
        [
            .. (await browser.GetAsync("/api/v1/library/years").JsonAsync()).GetProperty("years").EnumerateArray().Select(year => (
                year.GetProperty("year").GetInt32(),
                year.GetProperty("seconds").GetInt64(),
                year.GetProperty("source").GetString()!,
                year.GetProperty("first_day").GetString()!,
                year.GetProperty("last_day").GetString()!,
                year.GetProperty("whole").GetBoolean())),
        ];
}
