using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

        // With the instance's key: the owner gave none of their own.
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
        var tooSoon = await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null);
        Assert.Equal("60", tooSoon.Headers.RetryAfter?.ToString());
        Assert.Equal((HttpStatusCode.TooManyRequests, "too_many_requests"), await Task.FromResult(tooSoon).RefusalAsync());
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
    public async Task A_game_steam_no_longer_lists_is_flagged_and_never_taken_out()
    {
        var (steam, clock) = (new FakeSteam { Body = Library((620, "Portal 2", 90), (400, "Portal", 30)) }, new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        Assert.Equal(1, await TurnAsync(app));
        Assert.Null(await MissingSinceAsync(browser, "Portal"));

        // Steam now answers without Portal, in an answer that agrees with itself: refunded, or cut
        // short. Day after day, and asked for by hand: the game stays, flagged since the first.
        steam.Body = Library((620, "Portal 2", 90));
        clock.NextDay();
        var first = clock.Now;
        Assert.Equal(1, await TurnAsync(app));
        for (var day = 0; day < 3; day++)
        {
            clock.NextDay();
            Assert.Equal(1, await TurnAsync(app));
        }

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HttpStatusCode.OK, await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null).StatusAsync());
        Assert.Equal(["Portal", "Portal 2"], await NamesAsync(browser));
        Assert.Equal(first, await MissingSinceAsync(browser, "Portal"));
        Assert.Null(await MissingSinceAsync(browser, "Portal 2"));

        // Listed again: the flag goes.
        steam.Body = Library((620, "Portal 2", 90), (400, "Portal", 30));
        clock.NextDay();
        Assert.Equal(1, await TurnAsync(app));
        Assert.Null(await MissingSinceAsync(browser, "Portal"));
    }

    [Fact]
    public async Task A_sync_started_goes_on_when_its_request_is_given_up()
    {
        var (steam, clock) = (new FakeSteam { Body = Library((620, "Portal 2", 90)) }, new TestClock());
        // Steam is slow, and the visitor leaves the page while it is asked: the connection is cut,
        // as the server sees it, once Steam has been reached.
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answers = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cut = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        steam.Before = () =>
        {
            reached.TrySetResult();
            return answers.Task;
        };
        await using var app = await steam.StartAppAsync(null, services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IStartupFilter>(new CutConnection(reached.Task, cut));
        });
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        using var leaving = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/linked-accounts/{account}/sync");
        leaving.Headers.Add(CutConnection.Header, "1");
        var request = browser.SendAsync(leaving);
        // The request's own cancellation fired on the server, and the reading did not stop for it.
        Assert.True(await cut.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        // The reading is still on its way: asking again does not start another.
        Assert.Equal(
            (HttpStatusCode.Conflict, "sync_in_progress"),
            await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null).RefusalAsync());

        // Steam answers at last: the library is recorded and the run kept, with nobody waiting.
        answers.SetResult();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        for (var wait = 0; wait < 100 && !await db.SyncRuns.AnyAsync(); wait++)
        {
            await Task.Delay(50);
        }

        var run = await db.SyncRuns.AsNoTracking().SingleAsync();
        Assert.Equal(("manual", "succeeded"), (run.Trigger, run.Outcome));
        Assert.Equal([("Portal 2", 5400L)], await PlaytimesAsync(browser));

        // And giving up is no way around the pause: Steam was asked once.
        Assert.Equal(
            (HttpStatusCode.TooManyRequests, "too_many_requests"),
            await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null).RefusalAsync());
        Assert.Single(steam.Requests);

        // The visitor who left got no answer.
        await Assert.ThrowsAnyAsync<Exception>(() => request);
    }

    [Fact]
    public async Task A_reading_whose_run_cannot_be_kept_leaves_nothing_and_is_not_made_again_at_every_turn()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        await using var app = await steam.StartAppAsync(null, services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddScoped<ISyncRunStore, FailingRuns>();
        });
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        // Steam answers, the library is recorded, and the run cannot be written: all of it is undone.
        Assert.Equal(0, await TurnAsync(app));
        Assert.Contains("did not end", app.Logs.Text, StringComparison.Ordinal);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Equal((0, 0, 0), (await db.RawSnapshots.CountAsync(), await db.Ownerships.CountAsync(), await db.Observations.CountAsync()));

        // The account is still due, and Steam is not asked again at every turn for that: an hour later.
        Assert.Equal([account], await DueAsync(app));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, await TurnAsync(app));
        clock.Advance(TimeSpan.FromMinutes(50));
        Assert.Equal(0, await TurnAsync(app));
        Assert.Single(steam.Requests);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, await TurnAsync(app));
        Assert.Equal(2, steam.Requests.Count);
    }

    [Fact]
    public async Task An_answer_the_catalogue_refuses_is_a_failed_sync_kept_once()
    {
        var (store, clock) = (new OtherStore(), new TestClock());
        await using var app = await TestApp.StartAsync(null, services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<ILibrarySource>(store);
        });
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser, "player-1", source: store.Source);
        // A connector gone wrong: a playtime below zero.
        store.Answer = () => Task.FromResult(new LibraryReading(
            "library", clock.Now, "{}", [new OwnedItem("1", "A game", TimeSpan.FromMinutes(-5), null)], Complete: true));

        Assert.Equal(1, await TurnAsync(app));
        clock.Advance(TimeSpan.FromHours(3));
        Assert.Equal(0, await TurnAsync(app));
        Assert.Equal(1, store.Asked);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var run = await db.SyncRuns.AsNoTracking().SingleAsync();
        Assert.Equal((account, "failed", "unreadable", null), (run.LinkedAccountId, run.Outcome, run.Error, run.SnapshotId));
        Assert.StartsWith("the reading could not be recorded", run.Detail, StringComparison.Ordinal);
        Assert.Equal(0, await db.RawSnapshots.CountAsync());
    }

    [Fact]
    public async Task An_account_unlinked_while_it_is_read_keeps_nothing()
    {
        var (store, clock) = (new OtherStore(), new TestClock());
        await using var app = await TestApp.StartAsync(null, services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<ILibrarySource>(store);
        });
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser, "player-1", source: store.Source);
        store.Answer = async () =>
        {
            // Its owner unlinks it while the store is answering.
            Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync($"/api/v1/linked-accounts/{account}").StatusAsync());
            return new LibraryReading("library", clock.Now, "{}", [new OwnedItem("1", "A game", TimeSpan.FromMinutes(5), null)], Complete: true);
        };

        Assert.Equal(0, await TurnAsync(app));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Equal(
            (0, 0, 0, 0),
            (await db.SyncRuns.CountAsync(), await db.RawSnapshots.CountAsync(), await db.Ownerships.CountAsync(), await db.LinkedAccounts.CountAsync()));
        Assert.DoesNotContain("did not end", app.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reading_made_late_in_a_day_does_not_hold_back_the_next_days()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        var day = SyncSchedule.DayOf(clock.Now);

        // Steam did not answer all day, and does five minutes before the next day starts.
        steam.Unreachable = true;
        clock.Now = day.AddHours(22).AddMinutes(55);
        Assert.Equal(1, await TurnAsync(app));
        steam.Unreachable = false;
        clock.Now = day.AddHours(23).AddMinutes(55);
        Assert.Equal(1, await TurnAsync(app));

        // The new day's reading is made at its first turn, ten minutes later.
        clock.Now = day.AddDays(1).AddMinutes(5);
        Assert.Equal([account], await DueAsync(app));
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(3, steam.Requests.Count);
    }

    [Fact]
    public async Task The_pause_holds_for_a_sync_that_left_no_run()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        await using var app = await steam.StartAppAsync(null, services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddScoped<ISyncRunStore, FailingRuns>();
        });
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        // Steam is asked, and the run cannot be written: nothing in the database says a sync ran.
        Assert.Equal(
            (HttpStatusCode.InternalServerError, "internal_error"),
            await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null).RefusalAsync());
        Assert.Equal(
            (HttpStatusCode.TooManyRequests, "too_many_requests"),
            await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null).RefusalAsync());
        Assert.Single(steam.Requests);

        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(
            (HttpStatusCode.InternalServerError, "internal_error"),
            await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null).RefusalAsync());
        Assert.Equal(2, steam.Requests.Count);
    }

    [Fact]
    public async Task The_app_stopping_while_a_library_is_read_is_not_an_error()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        steam.Before = () =>
        {
            reached.TrySetResult();
            return Task.Delay(Timeout.Infinite);
        };

        // Taken while the app runs: once stopped, it hands out nothing.
        var gate = app.Services.GetRequiredService<SyncGate>();
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

        var request = browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        lifetime.StopApplication();

        // Whoever was waiting gets no answer, the log no error, and the account is free again.
        await Assert.ThrowsAnyAsync<Exception>(() => request);
        Assert.DoesNotContain("unhandled error", app.Logs.Text, StringComparison.Ordinal);
        Assert.True(gate.TryEnter(account));
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
        Assert.Equal(["200", "401", "403", "404", "409", "429"], sync.EnumerateObject().Select(response => response.Name).Order());
        var run = document.GetProperty("components").GetProperty("schemas").GetProperty("SyncRunResponse").GetProperty("properties");
        Assert.Equal(
            ["error", "finished_at", "id", "linked_account_id", "outcome", "own_key", "started_at", "trigger"],
            run.EnumerateObject().Select(property => property.Name).Order());

        // Where each linked account's sync stands, with the list of them.
        var schemas = document.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.GetProperty("LinkedAccountResponse").GetProperty("properties").TryGetProperty("sync", out _));
        Assert.Equal(
            ["last", "last_succeeded_at", "running"],
            schemas.GetProperty("SyncStateResponse").GetProperty("properties").EnumerateObject().Select(property => property.Name).Order());
    }

    [Fact]
    public async Task A_linked_account_says_where_its_sync_stands()
    {
        var (steam, clock) = (new FakeSteam { Body = Library((620, "Portal 2", 90)) }, new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        // Never read.
        Assert.Equal((false, null, null, null, null), await SyncStateAsync(browser));

        // Being read: the schedule's reading is seen while Steam has not answered.
        var (reached, release) = (new TaskCompletionSource(), new TaskCompletionSource());
        steam.Before = () =>
        {
            reached.TrySetResult();
            return release.Task;
        };
        var turn = TurnAsync(app);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal((true, null, null, null, null), await SyncStateAsync(browser));
        release.SetResult();
        Assert.Equal(1, await turn);
        steam.Before = null;

        // Read: the run, and how fresh the library is.
        var read = clock.Now;
        Assert.Equal((false, "scheduled", "succeeded", null, read), await SyncStateAsync(browser));

        // Steam away: the last run says why, and the library is as fresh as it was.
        steam.Unreachable = true;
        clock.Advance(TimeSpan.FromMinutes(2));
        await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null);
        Assert.Equal((false, "manual", "failed", "unavailable", read), await SyncStateAsync(browser));

        // Nothing visible is not a success either.
        steam.Unreachable = false;
        steam.Body = Library();
        clock.Advance(TimeSpan.FromMinutes(2));
        await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null);
        Assert.Equal((false, "manual", "nothing_visible", null, read), await SyncStateAsync(browser));

        // Read again.
        steam.Body = Library((620, "Portal 2", 150));
        clock.Advance(TimeSpan.FromMinutes(2));
        await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null);
        Assert.Equal((false, "manual", "succeeded", null, clock.Now), await SyncStateAsync(browser));
    }

    private static async Task<(bool Running, string? Trigger, string? Outcome, string? Error, DateTimeOffset? LastSucceededAt)> SyncStateAsync(HttpClient browser)
    {
        var account = Assert.Single((await browser.GetAsync("/api/v1/linked-accounts").JsonAsync()).GetProperty("accounts").EnumerateArray());
        var sync = account.GetProperty("sync");
        string? Text(JsonElement element, string name) => element.GetProperty(name).GetString();
        var last = sync.GetProperty("last");
        return (
            sync.GetProperty("running").GetBoolean(),
            last.ValueKind == JsonValueKind.Null ? null : Text(last, "trigger"),
            last.ValueKind == JsonValueKind.Null ? null : Text(last, "outcome"),
            last.ValueKind == JsonValueKind.Null ? null : Text(last, "error"),
            sync.GetProperty("last_succeeded_at") is { ValueKind: JsonValueKind.String } at ? at.GetDateTimeOffset() : null);
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
    private static async Task<Guid> LinkAsync(
        TestApp app, HttpClient browser, string steamId = SteamId, string email = "first@example.org", string source = "steam")
    {
        var owner = (await browser.RegisterAsync(email).JsonAsync()).GetProperty("id").GetGuid();
        await using var scope = app.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<ILibraryService>().LinkAccountAsync(owner, source, steamId, null, default)).Id;
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

    /// <summary>Since when a game's entry is flagged as missing; null when it is not.</summary>
    private static async Task<DateTimeOffset?> MissingSinceAsync(HttpClient browser, string name)
    {
        var game = Assert.Single(await LibraryAsync(browser), game => game.GetProperty("name").GetString() == name);
        return game.GetProperty("entries")[0].GetProperty("missing_since") is { ValueKind: JsonValueKind.String } since
            ? since.GetDateTimeOffset()
            : null;
    }

    /// <summary>Cuts the connection of a request that carries its header, once a moment has come:
    /// what the server sees when a visitor leaves. It says whether the request's own cancellation
    /// fired, and lets the rest of the pipeline go on.</summary>
    private sealed class CutConnection(Task when, TaskCompletionSource<bool> cut) : IStartupFilter
    {
        public const string Header = "X-Test-Cut-Connection";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => builder =>
        {
            builder.Use(async (context, following) =>
            {
                if (!context.Request.Headers.ContainsKey(Header))
                {
                    await following(context);
                    return;
                }

                var rest = following(context);
                await when;
                context.Abort();
                cut.TrySetResult(context.RequestAborted.IsCancellationRequested);
                await rest;
            });
            next(builder);
        };
    }

    /// <summary>The runs cannot be written, and none was ever kept.</summary>
    private sealed class FailingRuns : ISyncRunStore
    {
        public Task AddAsync(SyncRun run, CancellationToken ct) => throw new InvalidOperationException("the run cannot be written");

        public Task<IReadOnlyList<SyncRun>> ListSinceAsync(DateTimeOffset since, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SyncRun>>([]);

        public Task<SyncRun?> FindLatestAsync(Guid linkedAccountId, CancellationToken ct) => Task.FromResult<SyncRun?>(null);

        public Task<IReadOnlyDictionary<Guid, LatestRuns>> FindLatestAsync(IReadOnlyCollection<Guid> linkedAccountIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, LatestRuns>>(linkedAccountIds.ToDictionary(id => id, _ => new LatestRuns(null, null)));
    }

    /// <summary>A connector to a store that is not Steam, answering what a test makes it say.</summary>
    private sealed class OtherStore : ILibrarySource
    {
        public string Source => "other";

        public TimeSpan RecentWindow => TimeSpan.FromDays(14);

        public Func<Task<LibraryReading>> Answer { get; set; } = () => throw new InvalidOperationException("no answer was set");

        public int Asked { get; private set; }

        public Task<LibraryReading> ReadLibraryAsync(string externalId, string? credential, CancellationToken ct)
        {
            Asked++;
            return Answer();
        }

        public bool IsCredential(string credential) => false;
    }
}
