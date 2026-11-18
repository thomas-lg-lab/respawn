using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Respawn.Games.Api;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Games.Ports;
using Respawn.Games.Tests.Support;
using Respawn.Testing;

namespace Respawn.Games.Tests;

/// <summary>The sync: a linked account's library read from its store once a day, and when its owner
/// asks. Steam answers from memory, and the clock is the test's: a turn of the schedule is run by
/// hand, as the app's timer would.</summary>
public sealed class SyncTests
{
    private const string SteamId = "76561190000000001";

    [Fact]
    public async Task Two_days_give_two_snapshots_and_a_playtime_difference()
    {
        var (steam, clock) = (new FakeSteam { Body = Library((620, "Portal 2", 90), (400, "Portal", 30)) }, new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        // Linked a moment ago: due at once, without waiting for the next day.
        Assert.Equal([account], await DueAsync(app));
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal([("Portal", 1800L), ("Portal 2", 5400L)], await PlaytimesAsync(browser));

        // The reading of the day is made: the later turns of that day ask Steam nothing.
        clock.Advance(TimeSpan.FromHours(5));
        Assert.Empty(await DueAsync(app));
        Assert.Equal(0, await TurnAsync(app));
        Assert.Single(steam.Requests);

        // The next day, an hour of Portal 2 later.
        steam.Body = Library((620, "Portal 2", 150), (400, "Portal", 30));
        clock.NextDay();
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal([("Portal", 1800L), ("Portal 2", 9000L)], await PlaytimesAsync(browser));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var snapshots = await db.RawSnapshots.AsNoTracking().OrderBy(s => s.TakenAt).ToListAsync();
        Assert.Equal(2, snapshots.Count);
        Assert.Equal(TimeSpan.FromHours(29), snapshots[1].TakenAt - snapshots[0].TakenAt);
        Assert.NotEqual(snapshots[0].Payload, snapshots[1].Payload);

        // The history: what each day's answer said of the game, and the hour between the two.
        var portal2 = (await db.StoreEntries.AsNoTracking().SingleAsync(e => e.ExternalId == "620")).Id;
        var said = await db.Observations.AsNoTracking().Where(o => o.StoreEntryId == portal2).ToDictionaryAsync(o => o.SnapshotId);
        Assert.Equal(3600L, said[snapshots[1].Id].KnownPlaytimeSeconds - said[snapshots[0].Id].KnownPlaytimeSeconds);

        // And the two runs, each with its answer.
        var runs = await db.SyncRuns.AsNoTracking().OrderBy(r => r.StartedAt).ToListAsync();
        Assert.Equal(
            [("scheduled", "succeeded", null, snapshots[0].Id), ("scheduled", "succeeded", null, snapshots[1].Id)],
            runs.Select(run => (run.Trigger, run.Outcome, run.Error, run.SnapshotId!.Value)).ToArray());
        Assert.All(runs, run => Assert.Equal(account, run.LinkedAccountId));

        // With the instance's key: nobody gives their own yet.
        Assert.All(steam.Requests, request => Assert.Equal(FakeSteam.Key, request.Key));
    }

    [Fact]
    public async Task A_day_of_the_sync_starts_in_the_small_hours()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        var day = SyncSchedule.DayOf(clock.Now);
        Assert.Equal(SyncSchedule.DayStartsAt, day.TimeOfDay);

        // Read late in the evening: not again at midnight, and again once the next day has started.
        clock.Now = day.AddHours(19);
        Assert.Equal(1, await TurnAsync(app));
        clock.Now = day.AddHours(23).AddMinutes(59);
        Assert.Empty(await DueAsync(app));
        clock.Now = day.AddDays(1);
        Assert.Equal([account], await DueAsync(app));

        // The app was stopped for three days: one reading when it is back, not three.
        clock.Now = day.AddDays(4).AddHours(2);
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(0, await TurnAsync(app));
        Assert.Equal(2, steam.Requests.Count);
    }

    [Fact]
    public async Task Every_linked_account_is_read_the_earliest_link_first()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var first = app.Browser();
        using var second = app.Browser();
        var one = await LinkAsync(app, first, SteamId);
        clock.Advance(TimeSpan.FromMinutes(1));
        OpenRegistration();
        var two = await LinkAsync(app, second, "76561190000000002", "second@example.org");

        Assert.Equal([one, two], await DueAsync(app));
        Assert.Equal(2, await TurnAsync(app));
        Assert.Equal(
            ["steamid=" + SteamId, "steamid=76561190000000002"],
            steam.Requests.Select(request => request.Address.Query.TrimStart('?').Split('&')[0]));
        Assert.Empty(await DueAsync(app));
    }

    [Fact]
    public async Task Sync_now_reads_the_library_at_once()
    {
        var (steam, clock) = (new FakeSteam { Body = Library((620, "Portal 2", 90)) }, new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        var before = clock.Now;

        var answer = await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        var run = await answer.JsonAsync();
        Assert.Equal(
            (account, "manual", "succeeded", JsonValueKind.Null),
            (run.GetProperty("linked_account_id").GetGuid(), run.GetProperty("trigger").GetString(), run.GetProperty("outcome").GetString(), run.GetProperty("error").ValueKind));
        Assert.Equal((before, before), (run.GetProperty("started_at").GetDateTimeOffset(), run.GetProperty("finished_at").GetDateTimeOffset()));
        Assert.Equal([("Portal 2", 5400L)], await PlaytimesAsync(browser));

        // Pressed again at once: Steam is not asked again.
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(
            (HttpStatusCode.TooManyRequests, "too_many_requests"),
            await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null).RefusalAsync());
        Assert.Single(steam.Requests);

        // A minute later it is.
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(HttpStatusCode.OK, await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null).StatusAsync());
        Assert.Equal(2, steam.Requests.Count);

        // It does not stand for the reading of the day, which is still to make.
        Assert.Equal([account], await DueAsync(app));
    }

    [Fact]
    public async Task Sync_now_is_for_the_owner_of_the_linked_account()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        using var other = app.Browser();
        using var nobody = app.Browser();
        var mine = await LinkAsync(app, browser);
        OpenRegistration();
        await other.RegisterAsync("other@example.org", "Other");

        Assert.Equal(
            (HttpStatusCode.Unauthorized, "not_signed_in"),
            await nobody.PostAsync($"/api/v1/linked-accounts/{mine}/sync", null).RefusalAsync());
        // Someone else's and one that does not exist are not told apart.
        Assert.Equal(
            (HttpStatusCode.NotFound, "unknown_linked_account"),
            await other.PostAsync($"/api/v1/linked-accounts/{mine}/sync", null).RefusalAsync());
        Assert.Equal(
            (HttpStatusCode.NotFound, "unknown_linked_account"),
            await browser.PostAsync($"/api/v1/linked-accounts/{Guid.NewGuid()}/sync", null).RefusalAsync());
        // A page of another site cannot ask in the visitor's name.
        using var forged = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/linked-accounts/{mine}/sync");
        forged.Headers.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal((HttpStatusCode.Forbidden, "cross_site_request"), await browser.SendAsync(forged).RefusalAsync());

        Assert.Empty(steam.Requests);
    }

    [Fact]
    public async Task A_linked_account_is_read_by_one_sync_at_a_time()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        // A sync of that account is on its way.
        var gate = app.Services.GetRequiredService<SyncGate>();
        Assert.True(gate.TryEnter(account));

        Assert.Equal(
            (HttpStatusCode.Conflict, "sync_in_progress"),
            await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null).RefusalAsync());
        Assert.Equal(0, await TurnAsync(app));
        Assert.Empty(steam.Requests);

        // Once it has ended, the turn that follows makes the reading.
        gate.Leave(account);
        Assert.Equal(1, await TurnAsync(app));
    }

    [Fact]
    public async Task A_profile_that_shows_nothing_is_not_a_successful_sync()
    {
        var (steam, clock) = (new FakeSteam { Body = Library((620, "Portal 2", 90)) }, new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        Assert.Equal(1, await TurnAsync(app));

        // Game details made private: Steam answers, and lists nothing.
        steam.Body = FakeSteam.Recorded("owned-games-private.json");
        clock.NextDay();
        Assert.Equal(1, await TurnAsync(app));
        clock.Advance(TimeSpan.FromMinutes(2));
        var asked = await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null).JsonAsync();
        Assert.Equal(("nothing_visible", JsonValueKind.Null), (asked.GetProperty("outcome").GetString(), asked.GetProperty("error").ValueKind));

        // The library is as it was, the answers are kept, and the runs say what happened.
        Assert.Equal([("Portal 2", 5400L)], await PlaytimesAsync(browser));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Equal(3, await db.RawSnapshots.CountAsync());
        var runs = await db.SyncRuns.AsNoTracking().OrderBy(r => r.StartedAt).ToListAsync();
        Assert.Equal(
            [("scheduled", "succeeded"), ("scheduled", "nothing_visible"), ("manual", "nothing_visible")],
            runs.Select(run => (run.Trigger, run.Outcome)).ToArray());
        Assert.All(runs, run => Assert.NotNull(run.SnapshotId));

        // Steam did answer: the day's reading is made, and is not made again before the next day.
        clock.Advance(TimeSpan.FromHours(3));
        Assert.Empty(await DueAsync(app));
        clock.NextDay();
        Assert.Equal([account], await DueAsync(app));
    }

    [Fact]
    public async Task Steam_not_answering_is_kept_and_tried_again_an_hour_later()
    {
        var (steam, clock) = (new FakeSteam { Unreachable = true }, new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        Assert.Equal(1, await TurnAsync(app));
        Assert.Contains("could not be read (unavailable)", app.Logs.Text, StringComparison.Ordinal);

        // Not at every turn: an hour later.
        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(0, await TurnAsync(app));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await TurnAsync(app));

        // Asked for by hand meanwhile: the answer is the sync, failed, not an error of the request.
        clock.Advance(TimeSpan.FromMinutes(5));
        var asked = await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null);
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        var run = await asked.JsonAsync();
        Assert.Equal(("failed", "unavailable"), (run.GetProperty("outcome").GetString(), run.GetProperty("error").GetString()));

        // Steam is back: the reading of the day is made, an hour after the last scheduled failure.
        steam.Unreachable = false;
        clock.Advance(TimeSpan.FromMinutes(55));
        Assert.Equal(1, await TurnAsync(app));
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(0, await TurnAsync(app));

        await using var scope = app.Services.CreateAsyncScope();
        var runs = await scope.ServiceProvider.GetRequiredService<GamesDbContext>().SyncRuns.AsNoTracking().OrderBy(r => r.StartedAt).ToListAsync();
        Assert.Equal(
            [("scheduled", "failed", "unavailable"), ("scheduled", "failed", "unavailable"), ("manual", "failed", "unavailable"), ("scheduled", "succeeded", null)],
            runs.Select(r => (r.Trigger, r.Outcome, r.Error)).ToArray());
        Assert.Equal("Steam did not answer", runs[0].Detail);
        Assert.Null(runs[0].SnapshotId);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "refused")]
    [InlineData(HttpStatusCode.NotFound, "unreadable")]
    public async Task A_failure_that_trying_again_would_repeat_waits_for_the_next_day(HttpStatusCode status, string error)
    {
        var (steam, clock) = (new FakeSteam { Status = status }, new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        Assert.Equal(1, await TurnAsync(app));
        clock.Advance(TimeSpan.FromHours(6));
        Assert.Equal(0, await TurnAsync(app));
        Assert.Single(steam.Requests);

        clock.NextDay();
        Assert.Equal([account], await DueAsync(app));

        await using var scope = app.Services.CreateAsyncScope();
        var run = await scope.ServiceProvider.GetRequiredService<GamesDbContext>().SyncRuns.AsNoTracking().SingleAsync();
        Assert.Equal(("failed", error), (run.Outcome, run.Error));
    }

    [Fact]
    public async Task Without_a_key_the_sync_fails_and_steam_is_not_asked()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        var nowhere = Path.Combine(Path.GetTempPath(), $"respawn-no-steam-key-{Guid.NewGuid():N}");
        await using var app = await StartAsync(steam, clock, new() { ["RESPAWN_STEAM_API_KEY"] = null, ["RESPAWN_STEAM_API_KEY_FILE"] = nowhere });
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        Assert.Equal(1, await TurnAsync(app));
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(0, await TurnAsync(app));
        var asked = await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null).JsonAsync();

        Assert.Equal(("failed", "not_configured"), (asked.GetProperty("outcome").GetString(), asked.GetProperty("error").GetString()));
        Assert.Empty(steam.Requests);
    }

    [Fact]
    public async Task Only_two_days_in_a_row_tell_that_a_game_is_gone()
    {
        var (steam, clock) = (new FakeSteam { Body = Library((620, "Portal 2", 90), (400, "Portal", 30)) }, new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        Assert.Equal(1, await TurnAsync(app));

        // Steam now answers without Portal, in an answer that agrees with itself. Asked for twice
        // by hand: nothing leaves the library.
        steam.Body = Library((620, "Portal 2", 90));
        for (var press = 0; press < 2; press++)
        {
            clock.Advance(TimeSpan.FromMinutes(2));
            Assert.Equal(HttpStatusCode.OK, await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null).StatusAsync());
        }

        Assert.Equal(["Portal", "Portal 2"], await NamesAsync(browser));

        // The next day's reading says the same: still there, once is not enough.
        clock.NextDay();
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(["Portal", "Portal 2"], await NamesAsync(browser));

        // And the day after: gone.
        clock.NextDay();
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(["Portal 2"], await NamesAsync(browser));
    }

    [Fact]
    public async Task Unlinking_forgets_the_syncs_too()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        Assert.Equal(1, await TurnAsync(app));
        steam.Unreachable = true;
        clock.Advance(TimeSpan.FromMinutes(2));
        await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null);

        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync($"/api/v1/linked-accounts/{account}").StatusAsync());

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Equal((0, 0, 0), (await db.SyncRuns.CountAsync(), await db.RawSnapshots.CountAsync(), await db.LinkedAccounts.CountAsync()));
        Assert.Empty(await DueAsync(app));
    }

    [Fact]
    public async Task Without_a_database_the_schedule_says_so_once_and_goes_on()
    {
        await using var app = TestApp.Offline();

        Assert.Equal(0, await TurnAsync(app));
        Assert.Equal(0, await TurnAsync(app));

        Assert.Equal(1, app.Logs.Count("library sync: nothing can be scheduled for now"));
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, await client.GetAsync("/healthz").StatusAsync());
    }

    [Fact]
    public async Task The_description_says_what_a_sync_answers()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();

        var document = await browser.GetAsync("/api/v1/openapi.json").JsonAsync();

        var sync = document.GetProperty("paths").GetProperty("/api/v1/linked-accounts/{id}/sync").GetProperty("post").GetProperty("responses");
        Assert.Equal(["200", "401", "404", "409", "429"], sync.EnumerateObject().Select(response => response.Name).Order());
        var run = document.GetProperty("components").GetProperty("schemas").GetProperty("SyncRunResponse").GetProperty("properties");
        Assert.Equal(
            ["error", "finished_at", "id", "linked_account_id", "outcome", "started_at", "trigger"],
            run.EnumerateObject().Select(property => property.Name).Order());
    }

    private static Task<TestApp> StartAsync(FakeSteam steam, TestClock clock, Dictionary<string, string?>? settings = null) =>
        steam.StartAppAsync(settings, services => services.AddSingleton<TimeProvider>(clock));

    /// <summary>Steam's answer for a library, in Steam's shape: an app id, a name, minutes played.</summary>
    private static string Library(params (int AppId, string Name, int Minutes)[] games) =>
        JsonSerializer.Serialize(new
        {
            response = new
            {
                game_count = games.Length,
                games = games.Select(game => new { appid = game.AppId, name = game.Name, playtime_forever = game.Minutes }),
            },
        });

    /// <summary>An account of the instance, signed in, with a Steam account linked to it.</summary>
    private static async Task<Guid> LinkAsync(TestApp app, HttpClient browser, string steamId = SteamId, string email = "first@example.org")
    {
        var owner = (await browser.RegisterAsync(email).JsonAsync()).GetProperty("id").GetGuid();
        await using var scope = app.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<ILibraryService>().LinkAccountAsync(owner, "steam", steamId, null, default)).Id;
    }

    private static void OpenRegistration() =>
        TestDatabase.Run("INSERT INTO accounts.settings (key, value) VALUES ('registration_open', 'true') ON CONFLICT DO NOTHING");

    /// <summary>One turn of the schedule, as the app's timer runs it. Returns how many syncs ended.</summary>
    private static Task<int> TurnAsync(TestApp app) =>
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<SyncScheduler>()).RunDueAsync(default);

    private static async Task<IReadOnlyList<Guid>> DueAsync(TestApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISyncService>().GetDueAsync(default);
    }

    private static async Task<JsonElement[]> LibraryAsync(HttpClient browser) =>
        [.. (await browser.GetAsync("/api/v1/library").JsonAsync()).GetProperty("games").EnumerateArray()];

    private static async Task<string[]> NamesAsync(HttpClient browser) =>
        [.. (await LibraryAsync(browser)).Select(game => game.GetProperty("name").GetString()!)];

    private static async Task<(string Name, long Seconds)[]> PlaytimesAsync(HttpClient browser) =>
        [.. (await LibraryAsync(browser)).Select(game => (game.GetProperty("name").GetString()!, game.GetProperty("playtime_seconds").GetInt64()))];
}
