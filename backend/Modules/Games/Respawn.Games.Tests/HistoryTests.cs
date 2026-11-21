using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Games.Infrastructure.Persistence.Migrations;
using Respawn.Games.Ports;
using Respawn.Games.Tests.Support;
using Respawn.Testing;

namespace Respawn.Games.Tests;

/// <summary>The history of a library: what was played day by day, read from the growth of the stores'
/// totals between two readings. No store is called: a reading is what a connector would hand over.</summary>
public sealed class HistoryTests
{
    private const string Store = "steam";

    [Fact]
    public async Task The_history_needs_a_session_and_starts_empty()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await browser.GetAsync("/api/v1/library/history").RefusalAsync());

        await browser.RegisterAsync();
        var history = await browser.GetAsync("/api/v1/library/history").JsonAsync();
        Assert.Equal(JsonValueKind.Null, history.GetProperty("tracking_since").ValueKind);
        Assert.Empty(history.GetProperty("days").EnumerateArray());
    }

    [Fact]
    public async Task Two_readings_a_day_apart_give_one_day_and_the_first_alone_gives_none()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");

        // The first reading is where the count starts: its totals were played nobody knows when.
        await RecordAsync(app, account.Id, Day(1), ("620", "Portal 2", 90), ("400", "Portal", 30), ("220", "Half-Life 2", null));
        Assert.Empty(await DaysAsync(browser));
        Assert.Equal(Day(1), await SinceAsync(browser));

        // An hour of Portal 2 later; Portal was not played, and is not in the day.
        await RecordAsync(app, account.Id, Day(2), ("620", "Portal 2", 150), ("400", "Portal", 30), ("220", "Half-Life 2", null));
        var games = await GamesAsync(app);
        Assert.Equal([(Date(2), games["620"], 3600L)], await DaysAsync(browser));

        // A day with nothing played is left out, and the days come the earliest first, a day's games the
        // most played first. A game seen for the first time with a total starts there, like the others.
        await RecordAsync(app, account.Id, Day(3), ("620", "Portal 2", 150), ("400", "Portal", 30));
        await RecordAsync(app, account.Id, Day(4), ("620", "Portal 2", 160), ("400", "Portal", 75), ("220", "Half-Life 2", 20), ("70", "Half-Life", 500));
        await RecordAsync(app, account.Id, Day(5), ("220", "Half-Life 2", 25));
        Assert.Equal(
            [(Date(2), games["620"], 3600L), (Date(4), games["400"], 2700L), (Date(4), games["620"], 600L), (Date(5), games["220"], 300L)],
            await DaysAsync(browser));

        // The same start as the library's figures.
        Assert.Equal(Day(1), await SinceAsync(browser));
        Assert.Equal(
            Day(1),
            (await browser.GetAsync("/api/v1/library/stats").JsonAsync()).GetProperty("tracking_since").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_store_answering_zero_between_two_good_days_gives_no_negative_and_no_time_twice()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");

        await RecordAsync(app, account.Id, Day(1), ("620", "Portal 2", 90));
        await RecordAsync(app, account.Id, Day(2), ("620", "Portal 2", 120));
        // A bad day of the store: zero, then nothing said at all.
        await RecordAsync(app, account.Id, Day(3), ("620", "Portal 2", 0));
        await RecordAsync(app, account.Id, Day(4), ("620", "Portal 2", null));
        await RecordAsync(app, account.Id, Day(5), ("620", "Portal 2", 130));

        // Ten minutes on the day the store says again, not a hundred and thirty.
        var portal2 = (await GamesAsync(app))["620"];
        Assert.Equal([(Date(2), portal2, 1800L), (Date(5), portal2, 600L)], await DaysAsync(browser));

        // A store that corrects itself downwards takes nothing back, and what follows counts from there.
        await RecordAsync(app, account.Id, Day(6), ("620", "Portal 2", 100));
        await RecordAsync(app, account.Id, Day(7), ("620", "Portal 2", 105));
        Assert.Equal([(Date(2), portal2, 1800L), (Date(5), portal2, 600L), (Date(7), portal2, 300L)], await DaysAsync(browser));
    }

    [Fact]
    public async Task A_store_that_hid_the_playtime_does_not_give_it_all_on_the_day_it_shows_it()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");

        // Playtime kept private: every game is listed, with zero. Nothing is known yet.
        await RecordAsync(app, account.Id, Day(1), ("620", "Portal 2", 0), ("400", "Portal", 0));
        await RecordAsync(app, account.Id, Day(2), ("620", "Portal 2", 0), ("400", "Portal", 0));
        // The store says: years of play, which were not played that day. Portal was never launched.
        await RecordAsync(app, account.Id, Day(3), ("620", "Portal 2", 6000), ("400", "Portal", 0));
        Assert.Empty(await DaysAsync(browser));

        // The count starts there, for the game never launched too.
        await RecordAsync(app, account.Id, Day(4), ("620", "Portal 2", 6030), ("400", "Portal", 45));
        var games = await GamesAsync(app);
        Assert.Equal([(Date(4), games["400"], 2700L), (Date(4), games["620"], 1800L)], await DaysAsync(browser));
    }

    [Fact]
    public async Task A_reading_that_arrives_late_and_says_zero_counts_nothing_twice()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");
        await RecordAsync(app, account.Id, Day(1), ("620", "Portal 2", 90), ("400", "Portal", 30));
        await RecordAsync(app, account.Id, Day(4), ("620", "Portal 2", 150), ("400", "Portal", 30));
        var games = await GamesAsync(app);
        Assert.Equal([(Date(4), games["620"], 3600L)], await DaysAsync(browser));

        // A reading of the day between, recorded after: a poor one, which changes nothing.
        await RecordAsync(app, account.Id, Day(2), ("620", "Portal 2", 0), ("400", "Portal", 30));
        Assert.Equal([(Date(4), games["620"], 3600L)], await DaysAsync(browser));

        // A good one puts its part of the hour on its own day.
        await RecordAsync(app, account.Id, Day(3), ("620", "Portal 2", 110), ("400", "Portal", 30));
        Assert.Equal([(Date(3), games["620"], 1200L), (Date(4), games["620"], 2400L)], await DaysAsync(browser));
    }

    [Fact]
    public async Task The_zeros_recorded_before_are_corrected_by_the_migration()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");

        // A store hiding the playtime, then saying it; Portal is never launched until the fourth day.
        await RecordAsync(app, account.Id, Day(1), ("620", "Portal 2", 0), ("400", "Portal", 0));
        await RecordAsync(app, account.Id, Day(2), ("620", "Portal 2", 6000), ("400", "Portal", 0));
        await RecordAsync(app, account.Id, Day(5), ("620", "Portal 2", 6030), ("400", "Portal", 45));
        // And a reading of a day between, recorded after, poor for one game and good for the other.
        await RecordAsync(app, account.Id, Day(4), ("620", "Portal 2", 0), ("400", "Portal", 10));
        var games = await GamesAsync(app);
        (DateOnly, Guid, long)[] right = [(Date(4), games["400"], 600L), (Date(5), games["400"], 2100L), (Date(5), games["620"], 1800L)];
        Assert.Equal(right, await DaysAsync(browser));

        // As these readings were recorded before: every zero said was kept as a known total, and the
        // history gave everything ever played to the day the store said, twice.
        TestDatabase.Run("UPDATE games.observations SET known_playtime_seconds = 0 WHERE playtime_seconds = 0 AND known_playtime_seconds IS NULL");
        Assert.Equal(
            [(Date(2), games["620"], 360000L), (Date(4), games["400"], 600L), (Date(5), games["620"], 361800L), (Date(5), games["400"], 2100L)],
            await DaysAsync(browser));

        // The migration's statement: the zeros that said nothing go, the one of the game never launched
        // stays, and Portal's ten minutes still count from it. A second time changes nothing.
        TestDatabase.Run(KnownZeros.Sql);
        Assert.Equal(right, await DaysAsync(browser));
        TestDatabase.Run(KnownZeros.Sql);
        Assert.Equal(right, await DaysAsync(browser));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Equal(
            [null, 0L, 600L, 2700L],
            await (
                from observation in db.Observations.AsNoTracking()
                join snapshot in db.RawSnapshots on observation.SnapshotId equals snapshot.Id
                join entry in db.StoreEntries on observation.StoreEntryId equals entry.Id
                where entry.ExternalId == "400"
                orderby snapshot.TakenAt
                select observation.KnownPlaytimeSeconds).ToArrayAsync());
    }

    [Fact]
    public async Task Two_entries_of_one_game_add_up()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var owner = await OwnerAsync(browser);
        var one = await LinkAsync(app, owner, "76561190000000001");
        var other = await LinkAsync(app, owner, "76561190000000002");

        // One game three ways: on two store accounts, and under a second entry merged into it.
        await RecordAsync(app, one.Id, Day(1), ("620", "Portal 2", 90), ("621", "Portal 2", 10));
        await RecordAsync(app, other.Id, Day(1), ("620", "Portal 2", 600));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var portal2 = (await GamesAsync(app))["620"];
        await db.StoreEntries.Where(e => e.ExternalId == "621").ExecuteUpdateAsync(entry => entry.SetProperty(e => e.GameId, portal2));

        // Each entry counts from its own total: the two accounts are never compared with each other.
        await RecordAsync(app, one.Id, Day(2), ("620", "Portal 2", 100), ("621", "Portal 2", 15));
        await RecordAsync(app, other.Id, Day(2).AddHours(1), ("620", "Portal 2", 620));
        Assert.Equal([(Date(2), portal2, (10 + 5 + 20) * 60L)], await DaysAsync(browser));
    }

    [Fact]
    public async Task Another_accounts_history_is_never_returned()
    {
        await using var app = await StartAsync();
        using var first = app.Browser();
        var one = await LinkAsync(app, await OwnerAsync(first), "76561190000000001");
        TestDatabase.Run("INSERT INTO accounts.settings (key, value) VALUES ('registration_open', 'true') ON CONFLICT DO NOTHING");
        using var second = app.Browser();
        var other = await LinkAsync(app, await OwnerAsync(second, "second@example.org"), "76561190000000002");
        using var third = app.Browser();
        await OwnerAsync(third, "third@example.org");

        // The same game, played by both, on different days.
        await RecordAsync(app, one.Id, Day(1), ("620", "Portal 2", 90));
        await RecordAsync(app, other.Id, Day(2), ("620", "Portal 2", 600));
        await RecordAsync(app, one.Id, Day(3), ("620", "Portal 2", 100));
        await RecordAsync(app, other.Id, Day(4), ("620", "Portal 2", 660));

        var portal2 = (await GamesAsync(app))["620"];
        Assert.Equal([(Date(3), portal2, 600L)], await DaysAsync(first));
        Assert.Equal([(Date(4), portal2, 3600L)], await DaysAsync(second));
        Assert.Equal(Day(1), await SinceAsync(first));
        Assert.Equal(Day(2), await SinceAsync(second));

        // And nothing for who linked no store account.
        Assert.Empty(await DaysAsync(third));
        Assert.Null(await SinceAsync(third));
    }

    [Fact]
    public async Task A_game_taken_out_of_the_library_is_out_of_its_history()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");
        await RecordAsync(app, account.Id, Day(1), ("620", "Portal 2", 90), ("400", "Portal", 30));
        await RecordAsync(app, account.Id, Day(2), ("620", "Portal 2", 100), ("400", "Portal", 60));
        var games = await GamesAsync(app);
        Assert.Equal([(Date(2), games["400"], 1800L), (Date(2), games["620"], 600L)], await DaysAsync(browser));

        // Its owner's decision, which no route takes yet: written here as the table holds it.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var portal = (await db.StoreEntries.AsNoTracking().SingleAsync(e => e.ExternalId == "400")).Id;
        TestDatabase.Run($"UPDATE games.ownerships SET unlisted_since = '{Day(3):O}' WHERE store_entry_id = '{portal}'");
        Assert.Equal([(Date(2), games["620"], 600L)], await DaysAsync(browser));
    }

    [Fact]
    public async Task A_part_of_the_history_is_asked_by_its_days()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");
        await RecordAsync(app, account.Id, Day(1), ("620", "Portal 2", 10));
        await RecordAsync(app, account.Id, Day(2), ("620", "Portal 2", 20));
        await RecordAsync(app, account.Id, Day(3), ("620", "Portal 2", 40));
        await RecordAsync(app, account.Id, Day(4), ("620", "Portal 2", 80));
        var portal2 = (await GamesAsync(app))["620"];

        // Both days are included, and a day still counts from the reading before it, asked or not.
        Assert.Equal([(Date(3), portal2, 1200L), (Date(4), portal2, 2400L)], await DaysAsync(browser, "?from=2026-11-03"));
        Assert.Equal([(Date(2), portal2, 600L), (Date(3), portal2, 1200L)], await DaysAsync(browser, "?to=2026-11-03"));
        Assert.Equal([(Date(3), portal2, 1200L)], await DaysAsync(browser, "?from=2026-11-03&to=2026-11-03"));
        Assert.Empty(await DaysAsync(browser, "?from=2026-11-04&to=2026-11-02"));
        // Where the history starts does not depend on the part asked.
        Assert.Equal(Day(1), await SinceAsync(browser, "?from=2026-11-04"));

        foreach (var query in new[] { "?from=yesterday", "?to=2026-13-01" })
        {
            Assert.Equal(
                (HttpStatusCode.UnprocessableEntity, "invalid_request"),
                await browser.GetAsync("/api/v1/library/history" + query).RefusalAsync());
        }
    }

    [Fact]
    public async Task The_description_says_what_the_history_answers()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var document = await browser.GetAsync("/api/v1/openapi.json").JsonAsync();

        var route = document.GetProperty("paths").GetProperty("/api/v1/library/history").GetProperty("get");
        var responses = route.GetProperty("responses");
        Assert.Equal(["200", "401", "422"], responses.EnumerateObject().Select(response => response.Name).Order());
        var answer = responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.EndsWith("/LibraryHistoryResponse", answer.GetProperty("$ref").GetString(), StringComparison.Ordinal);
        Assert.Equal(["from", "to"], route.GetProperty("parameters").EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString()!).ToArray());

        var schemas = document.GetProperty("components").GetProperty("schemas");
        string[] Fields(string schema) =>
            [.. schemas.GetProperty(schema).GetProperty("properties").EnumerateObject().Select(field => field.Name)];
        Assert.Equal(["tracking_since", "days"], Fields("LibraryHistoryResponse"));
        Assert.Equal(["date", "games"], Fields("HistoryDayResponse"));
        Assert.Equal(["game_id", "seconds"], Fields("HistoryGameResponse"));
        Assert.Equal("date", schemas.GetProperty("HistoryDayResponse").GetProperty("properties").GetProperty("date").GetProperty("format").GetString());
    }

    /// <summary>A day of the past, at the hour a daily reading would be taken.</summary>
    private static DateTimeOffset Day(int day) => new(2026, 11, day, 6, 0, 0, TimeSpan.Zero);

    private static DateOnly Date(int day) => new(2026, 11, day);

    /// <summary>The app, its clock set after every reading of <see cref="Day"/>, whatever the date the
    /// tests run.</summary>
    private static Task<TestApp> StartAsync() =>
        TestApp.StartAsync(services: services => services.AddSingleton<TimeProvider>(new TestClock { Now = Day(28) }));

    private static async Task<Guid> OwnerAsync(HttpClient browser, string email = "first@example.org") =>
        (await browser.RegisterAsync(email).JsonAsync()).GetProperty("id").GetGuid();

    private static Task<LinkedAccount> LinkAsync(TestApp app, Guid owner, string externalId) =>
        UseAsync(app, library => library.LinkAccountAsync(owner, Store, externalId, null, default));

    /// <summary>A reading of a library: each game with the total its store gives, in minutes, or none.</summary>
    private static Task<Guid> RecordAsync(
        TestApp app, Guid account, DateTimeOffset takenAt, params (string Id, string Name, int? Minutes)[] items)
    {
        var reading = new LibraryReading(
            "owned-games",
            takenAt,
            "{}",
            [.. items.Select(item => new OwnedItem(
                item.Id, item.Name, item.Minutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null, LastPlayedAt: null))]);
        return UseAsync(app, library => library.RecordLibraryAsync(account, reading, default));
    }

    private static async Task<T> UseAsync<T>(TestApp app, Func<ILibraryService, Task<T>> use)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<ILibraryService>());
    }

    /// <summary>The games of the catalogue, by the id their store entry has in the store.</summary>
    private static async Task<Dictionary<string, Guid>> GamesAsync(TestApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        return await db.StoreEntries.AsNoTracking().ToDictionaryAsync(e => e.ExternalId, e => e.GameId);
    }

    /// <summary>The history as the API answers it, flat: one item per day and game, in its order.</summary>
    private static async Task<(DateOnly Date, Guid Game, long Seconds)[]> DaysAsync(HttpClient browser, string query = "")
    {
        var history = await browser.GetAsync("/api/v1/library/history" + query).JsonAsync();
        return
        [
            .. history.GetProperty("days").EnumerateArray().SelectMany(day =>
            {
                // The day as the wire spells it: YYYY-MM-DD and nothing else.
                var date = DateOnly.ParseExact(day.GetProperty("date").GetString()!, "yyyy-MM-dd");
                var games = day.GetProperty("games").EnumerateArray().ToArray();
                Assert.NotEmpty(games);
                return games.Select(game => (date, game.GetProperty("game_id").GetGuid(), game.GetProperty("seconds").GetInt64()));
            }),
        ];
    }

    private static async Task<DateTimeOffset?> SinceAsync(HttpClient browser, string query = "") =>
        (await browser.GetAsync("/api/v1/library/history" + query).JsonAsync()).GetProperty("tracking_since") is { ValueKind: JsonValueKind.String } since
            ? since.GetDateTimeOffset()
            : null;
}
