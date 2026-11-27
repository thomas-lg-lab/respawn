using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Api;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Games.Ports;
using Respawn.Games.Tests.Support;
using Respawn.Testing;
using static Respawn.Games.Tests.Support.Fixtures;

namespace Respawn.Games.Tests;

/// <summary>One game as the signed-in account sees it (<c>GET /api/v1/games/{id}</c>), its months
/// (<c>GET /api/v1/games/{id}/months</c>), and one month of a library
/// (<c>GET /api/v1/library/months/{month}</c>): read from what the readings and the yearly reviews kept. Steam answers the reviews from memory, the readings are handed over as a connector
/// would, and the clock is the test's.</summary>
public sealed class GameTests
{
    /// <summary>A moment of 2026: the reviews asked for are those of 2022 to 2025, all settled.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_game_needs_a_session_and_one_the_catalogue_does_not_have_answers_404()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var unknown = $"/api/v1/games/{Guid.CreateVersion7()}";
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await browser.GetAsync(unknown).RefusalAsync());
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await browser.GetAsync(unknown + "/months").RefusalAsync());
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await browser.GetAsync("/api/v1/library/months/2026-11").RefusalAsync());

        await browser.RegisterAsync();
        Assert.Equal((HttpStatusCode.NotFound, "unknown_game"), await browser.GetAsync(unknown).RefusalAsync());
        Assert.Equal((HttpStatusCode.NotFound, "unknown_game"), await browser.GetAsync(unknown + "/months").RefusalAsync());
    }

    [Fact]
    public async Task A_game_comes_with_what_is_owned_of_it_its_rank_and_apart_its_months()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        // Three readings: an hour of Portal 2 on the second day, 45 minutes of Portal on the third.
        // No store says Half-Life 2 was ever played.
        await RecordAsync(app, account, Day(1), ("620", "Portal 2", 90), ("400", "Portal", 30), ("220", "Half-Life 2", null));
        await RecordAsync(app, account, Day(2), ("620", "Portal 2", 150), ("400", "Portal", 30), ("220", "Half-Life 2", null));
        await RecordAsync(app, account, Day(3), ("620", "Portal 2", 150), ("400", "Portal", 75), ("220", "Half-Life 2", null));
        var games = await GamesAsync(app);

        var portal2 = await GameAsync(browser, games["620"]);
        Assert.Equal(games["620"], portal2.GetProperty("game_id").GetGuid());
        Assert.Equal("Portal 2", portal2.GetProperty("name").GetString());
        Assert.Equal([("steam", "620")], Stores(portal2));
        Assert.Equal(9000L, portal2.GetProperty("playtime_seconds").GetInt64());
        Assert.Equal(JsonValueKind.Null, portal2.GetProperty("last_played_at").ValueKind);

        // Its entry, as the library gives it.
        var entry = Assert.Single(portal2.GetProperty("entries").EnumerateArray());
        Assert.Equal(
            (account, "steam", "620", 9000L),
            (entry.GetProperty("linked_account_id").GetGuid(), entry.GetProperty("source").GetString(), entry.GetProperty("external_id").GetString(), entry.GetProperty("playtime_seconds").GetInt64()));
        Assert.Equal(Day(1), entry.GetProperty("first_seen_at").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("missing_since").ValueKind);

        // The most played of the two games launched; a game never launched has no rank.
        Assert.Equal((1, 2), Rank(portal2));
        Assert.Equal((2, 2), Rank(await GameAsync(browser, games["400"])));
        Assert.Null(Rank(await GameAsync(browser, games["220"])));

        // No review yet: the month the readings started in. Portal's 45 minutes are not in it.
        Assert.Equal([("2026-11-01", 3600L, "readings")], await MonthsAsync(browser, games["620"]));
        Assert.Empty(await MonthsAsync(browser, games["220"]));

        // The reviews: 2024 from Steam, the other years reviewed with nothing played. The months of
        // 2024 add up to the game's part of the 3 h 15 min the library's years count for it.
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(
            [("2024-01-01", 5400L, "review"), ("2024-03-01", 1800L, "review"), ("2026-11-01", 3600L, "readings")],
            await MonthsAsync(browser, games["620"]));
        Assert.Equal(
            11700L,
            (await browser.GetAsync("/api/v1/library/years").JsonAsync()).GetProperty("years").EnumerateArray()
                .Single(year => year.GetProperty("year").GetInt32() == 2024).GetProperty("seconds").GetInt64());

        // A game never launched as the store counts today, and played in a year reviewed.
        var halfLife2 = await GameAsync(browser, games["220"]);
        Assert.Equal(JsonValueKind.Null, halfLife2.GetProperty("playtime_seconds").ValueKind);
        Assert.Single(halfLife2.GetProperty("entries").EnumerateArray());
        Assert.Equal([("2024-01-01", 3600L, "review")], await MonthsAsync(browser, games["220"]));

        // A game the review counted and the library does not own: no entry, no rank, its month.
        games = await GamesAsync(app);
        var halfLife = await GameAsync(browser, games["70"]);
        Assert.Equal(JsonValueKind.Null, halfLife.GetProperty("name").ValueKind);
        Assert.Equal([("steam", "70")], Stores(halfLife));
        Assert.Equal(JsonValueKind.Null, halfLife.GetProperty("playtime_seconds").ValueKind);
        Assert.Empty(halfLife.GetProperty("entries").EnumerateArray());
        Assert.Null(Rank(halfLife));
        Assert.Equal([("2024-03-01", 900L, "review")], await MonthsAsync(browser, games["70"]));

        // Another account is answered the catalogue's record, and nothing of the first one's.
        TestDatabase.OpenRegistration();
        using var other = app.Browser();
        await other.RegisterAsync("second@example.org");
        var seen = await GameAsync(other, games["620"]);
        Assert.Equal("Portal 2", seen.GetProperty("name").GetString());
        Assert.Equal([("steam", "620")], Stores(seen));
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("playtime_seconds").ValueKind);
        Assert.Empty(seen.GetProperty("entries").EnumerateArray());
        Assert.Null(Rank(seen));
        Assert.Empty(await MonthsAsync(other, games["620"]));
    }

    [Fact]
    public async Task A_game_owned_through_two_accounts_adds_up_and_says_which_entry_is_missing()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var first = await LinkAsync(app, browser);
        var owner = await OwnerAsync(app, first);
        var second = (await UseAsync(app, library => library.LinkAccountAsync(owner, "steam", "76561190000000002", null, default))).Id;

        await RecordAsync(app, first, Day(1), ("620", "Portal 2", 90), ("400", "Portal", 30));
        await RecordAsync(app, second, Day(1), ("620", "Portal 2", 100), ("70", "Half-Life", 30));
        // A complete reading of the first account that leaves Portal out; ten minutes of Portal 2.
        await RecordAsync(app, first, Day(2), complete: true, ("620", "Portal 2", 100));
        var games = await GamesAsync(app);

        // The total over both accounts, and each account's own.
        var portal2 = await GameAsync(browser, games["620"]);
        Assert.Equal(12000L, portal2.GetProperty("playtime_seconds").GetInt64());
        Assert.Equal(
            new[] { (first, 6000L), (second, 6000L) }.OrderBy(entry => entry.Item1),
            portal2.GetProperty("entries").EnumerateArray()
                .Select(entry => (entry.GetProperty("linked_account_id").GetGuid(), entry.GetProperty("playtime_seconds").GetInt64()))
                .OrderBy(entry => entry.Item1));
        Assert.Equal([("2026-11-01", 600L, "readings")], await MonthsAsync(browser, games["620"]));

        // The game its store no longer lists stays, flagged on its entry.
        var portal = await GameAsync(browser, games["400"]);
        Assert.Equal(Day(2), Assert.Single(portal.GetProperty("entries").EnumerateArray()).GetProperty("missing_since").GetDateTimeOffset());

        // Two games played as long share their position, counted among the three launched.
        Assert.Equal((1, 3), Rank(portal2));
        Assert.Equal((2, 3), Rank(portal));
        Assert.Equal((2, 3), Rank(await GameAsync(browser, games["70"])));
    }

    [Fact]
    public async Task A_month_is_answered_from_what_counts_its_year()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        await RecordAsync(app, account, Day(1), ("620", "Portal 2", 90), ("400", "Portal", 30), ("220", "Half-Life 2", null));
        await RecordAsync(app, account, Day(2), ("620", "Portal 2", 150), ("400", "Portal", 30), ("220", "Half-Life 2", null));
        await RecordAsync(app, account, Day(3), ("620", "Portal 2", 150), ("400", "Portal", 75), ("220", "Half-Life 2", null));
        Assert.Equal(1, await TurnAsync(app));
        var games = await GamesAsync(app);

        // A month of a year reviewed: every game the review counts, the most played first, with the
        // name the catalogue has; a review names none, so a game the library never listed has none.
        await MonthIsAsync(browser, "2024-01", ("review", "2024-01-01", "2024-01-31"), (games["620"], "Portal 2", 5400L), (games["220"], "Half-Life 2", 3600L));
        await MonthIsAsync(browser, "2024-03", ("review", "2024-03-01", "2024-03-31"), (games["620"], "Portal 2", 1800L), (games["70"], null, 900L));
        await MonthIsAsync(browser, "2025-06", ("review", "2025-06-01", "2025-06-30"));

        // The month the readings started in: its days up to today, and the games of the library.
        await MonthIsAsync(browser, "2026-11", ("readings", "2026-11-01", "2026-11-28"), (games["620"], "Portal 2", 3600L), (games["400"], "Portal", 2700L));

        // Nothing counts a month before the first reading in a year with no review, one still to
        // come, or one before any review.
        foreach (var month in new[] { "2026-10", "2026-12", "2027-01", "2021-05" })
        {
            await MonthIsAsync(browser, month, (null, null, null));
        }

        // Another account sees nothing of it.
        TestDatabase.OpenRegistration();
        using var other = app.Browser();
        await other.RegisterAsync("second@example.org");
        await MonthIsAsync(other, "2024-01", (null, null, null));

        foreach (var month in new[] { "2026-13", "2026-1", "november", "0000-01" })
        {
            Assert.Equal(
                (HttpStatusCode.UnprocessableEntity, "invalid_request"),
                await browser.GetAsync("/api/v1/library/months/" + month).RefusalAsync());
        }
    }

    [Fact]
    public async Task The_description_says_what_a_game_and_a_month_answer()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var document = await browser.GetAsync("/api/v1/openapi.json").JsonAsync();
        var paths = document.GetProperty("paths");
        string[] Statuses(string path) =>
            [.. paths.GetProperty(path).GetProperty("get").GetProperty("responses").EnumerateObject().Select(response => response.Name).Order()];
        Assert.Equal(["200", "401", "404"], Statuses("/api/v1/games/{id}"));
        Assert.Equal(["200", "401", "404"], Statuses("/api/v1/games/{id}/months"));
        Assert.Equal(["200", "401", "422"], Statuses("/api/v1/library/months/{month}"));

        // The routes that take a game out of the library and answer its return.
        string[] StatusesOf(string path, string method) =>
            [.. paths.GetProperty(path).GetProperty(method).GetProperty("responses").EnumerateObject().Select(response => response.Name).Order()];
        Assert.Equal(["204", "401", "403", "404", "409"], StatusesOf("/api/v1/library/games/{id}", "delete"));
        Assert.Equal(["204", "401", "403", "404"], StatusesOf("/api/v1/library/games/{id}", "put"));
        Assert.Equal(["200", "401"], StatusesOf("/api/v1/library/removed", "get"));
        Assert.Equal(["204", "401", "403", "404", "409"], StatusesOf("/api/v1/library/removed/{id}/notice", "delete"));

        var schemas = document.GetProperty("components").GetProperty("schemas");
        string[] Fields(string schema) =>
            [.. schemas.GetProperty(schema).GetProperty("properties").EnumerateObject().Select(field => field.Name)];
        Assert.Equal(
            ["game_id", "name", "stores", "playtime_seconds", "recent_playtime_seconds", "last_played_at", "entries", "rank", "removed", "game_data", "covers"],
            Fields("GameResponse"));
        Assert.Equal(["source", "url"], Fields("GameDataPageResponse"));
        Assert.Equal(["sources", "chosen"], Fields("GameCoversResponse"));
        Assert.Equal(["source"], Fields("CoverChoiceRequest"));
        Assert.Equal(["200", "401", "403", "404", "409", "422"], StatusesOf("/api/v1/games/{id}/cover-choice", "put"));
        Assert.Equal(["200", "401", "403", "404"], StatusesOf("/api/v1/games/{id}/cover-choice", "delete"));
        Assert.Equal(["200", "401"], Statuses("/api/v1/credits"));
        Assert.Equal(["credits"], Fields("CreditsResponse"));
        Assert.Equal(["source", "label", "url"], Fields("CreditResponse"));
        Assert.Equal(["store_entry_id", "source", "external_id"], Fields("GameStoreResponse"));
        Assert.Equal(["position", "launched"], Fields("GameRankResponse"));
        Assert.Equal(["games"], Fields("RemovedGamesResponse"));
        Assert.Equal(["game_id", "name", "back_since", "entries"], Fields("RemovedGameResponse"));
        Assert.Equal(
            ["store_entry_id", "linked_account_id", "source", "external_id", "removed_at", "listed_again_since", "notice_dismissed_at"],
            Fields("RemovedEntryResponse"));
        Assert.Equal(["months"], Fields("GameMonthsResponse"));
        Assert.Equal(["month", "seconds", "source"], Fields("GameMonthResponse"));
        Assert.Equal(["month", "source", "first_day", "last_day", "games"], Fields("LibraryMonthResponse"));
        Assert.Equal(["game_id", "name", "seconds"], Fields("MonthGameResponse"));
        Assert.Equal("date", schemas.GetProperty("GameMonthResponse").GetProperty("properties").GetProperty("month").GetProperty("format").GetString());
    }

    /// <summary>A day of November 2026, at the hour a daily reading would be taken.</summary>
    private static DateTimeOffset Day(int day) => new(2026, 11, day, 6, 0, 0, TimeSpan.Zero);

    /// <summary>The app, Steam having a review of 2024 and of no other year.</summary>
    private static Task<TestApp> StartAsync()
    {
        var steam = new FakeSteam
        {
            BodyFor = address => address.AbsolutePath.Contains("GetUserYearInReview", StringComparison.Ordinal)
                ? FakeSteam.Recorded(address.Query.Contains("year=2024", StringComparison.Ordinal) ? "year-in-review-2024.json" : "year-in-review-none.json")
                : null,
        };
        return steam.StartAppAsync(services: services => services.AddSingleton<TimeProvider>(new TestClock { Now = Now }));
    }

    /// <summary>A Steam account linked to a new owner, its first sync ended: Steam shows its library.</summary>
    private static async Task<Guid> LinkAsync(TestApp app, HttpClient browser)
    {
        var account = await Fixtures.LinkAsync(app, browser);
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISyncRunStore>().AddAsync(
            new SyncRun(Guid.CreateVersion7(), account, SyncTrigger.Scheduled, Now, Now, SyncOutcome.Succeeded),
            default);
        return account;
    }

    private static Task<Guid> RecordAsync(
        TestApp app, Guid account, DateTimeOffset takenAt, params (string Id, string Name, int? Minutes)[] items) =>
        RecordAsync(app, account, takenAt, complete: false, items);

    /// <summary>A reading of a library: each game with the total its store gives, in minutes, or none.</summary>
    private static Task<Guid> RecordAsync(
        TestApp app, Guid account, DateTimeOffset takenAt, bool complete, params (string Id, string Name, int? Minutes)[] items)
    {
        var reading = new LibraryReading(
            "owned-games",
            takenAt,
            "{}",
            [.. items.Select(item => new OwnedItem(
                item.Id, item.Name, item.Minutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null, LastPlayedAt: null))],
            complete);
        return UseAsync(app, library => library.RecordLibraryAsync(account, reading, default));
    }

    /// <summary>One turn of the import of the reviews, as the app's timer runs it.</summary>
    private static Task<int> TurnAsync(TestApp app) =>
        TurnAsync<YearReviewScheduler>(app);

    /// <summary>The games of the catalogue, by the id their store entry has in the store.</summary>
    private static async Task<Dictionary<string, Guid>> GamesAsync(TestApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        return await db.StoreEntries.AsNoTracking().ToDictionaryAsync(e => e.ExternalId, e => e.GameId);
    }

    private static Task<JsonElement> GameAsync(HttpClient browser, Guid game) =>
        browser.GetAsync($"/api/v1/games/{game}").JsonAsync();

    private static (string Source, string ExternalId)[] Stores(JsonElement game) =>
        [.. game.GetProperty("stores").EnumerateArray().Select(store => (store.GetProperty("source").GetString()!, store.GetProperty("external_id").GetString()!))];

    private static (int Position, int Launched)? Rank(JsonElement game) =>
        game.GetProperty("rank") is { ValueKind: JsonValueKind.Object } rank
            ? (rank.GetProperty("position").GetInt32(), rank.GetProperty("launched").GetInt32())
            : null;

    /// <summary>The months of a game as the API answers them, in its order.</summary>
    private static async Task<(string Month, long Seconds, string Source)[]> MonthsAsync(HttpClient browser, Guid game) =>
        [
            .. (await browser.GetAsync($"/api/v1/games/{game}/months").JsonAsync()).GetProperty("months").EnumerateArray().Select(month => (
                month.GetProperty("month").GetString()!,
                month.GetProperty("seconds").GetInt64(),
                month.GetProperty("source").GetString()!)),
        ];

    /// <summary>Checks a month as the API answers it: what counts it, and its games in its order.</summary>
    private static async Task MonthIsAsync(
        HttpClient browser,
        string month,
        (string? Source, string? FirstDay, string? LastDay) counted,
        params (Guid Game, string? Name, long Seconds)[] games)
    {
        var answer = await browser.GetAsync("/api/v1/library/months/" + month).JsonAsync();
        Assert.Equal(month + "-01", answer.GetProperty("month").GetString());
        Assert.Equal(
            counted,
            (answer.GetProperty("source").GetString(), answer.GetProperty("first_day").GetString(), answer.GetProperty("last_day").GetString()));
        Assert.Equal(
            games,
            answer.GetProperty("games").EnumerateArray().Select(game => (
                game.GetProperty("game_id").GetGuid(),
                game.GetProperty("name").GetString(),
                game.GetProperty("seconds").GetInt64())));
    }
}
