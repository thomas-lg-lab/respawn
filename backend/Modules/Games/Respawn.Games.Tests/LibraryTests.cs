using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Games.Ports;
using Respawn.Games.Tests.Support;
using Respawn.Testing;
using static Respawn.Games.Tests.Support.Fixtures;

namespace Respawn.Games.Tests;

/// <summary>Libraries: a store account linked to an owner, and what a store's answer becomes. No store is
/// called: a reading is what a connector would hand over.</summary>
public sealed class LibraryTests
{
    private const string Store = "steam";

    // Spaces, key order and a repeated key as the source sent them: none of it may change once stored.
    private const string Payload = "{ \"response\": {\"game_count\":2,  \"games\":[ {\"appid\":620, \"appid\":620},{\"appid\":400} ]} }\n";

    private static readonly DateTimeOffset LastPlayed = new(2026, 10, 30, 20, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_library_needs_a_session()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await browser.GetAsync("/api/v1/library").RefusalAsync());

        await browser.RegisterAsync();
        Assert.Empty((await browser.GetAsync("/api/v1/library").JsonAsync()).GetProperty("games").EnumerateArray());
    }

    [Fact]
    public async Task The_description_says_what_the_library_answers()
    {
        // The front's types are generated from the description: a route that declares its refusal
        // must still describe its answer.
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var document = await browser.GetAsync("/api/v1/openapi.json").JsonAsync();
        var responses = document.GetProperty("paths").GetProperty("/api/v1/library").GetProperty("get").GetProperty("responses");

        Assert.True(responses.TryGetProperty("401", out _));
        var answer = responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.EndsWith("/LibraryResponse", answer.GetProperty("$ref").GetString(), StringComparison.Ordinal);
        var schemas = document.GetProperty("components").GetProperty("schemas");
        // The fields, whatever the case the description spells them in.
        string[] Fields(string schema) =>
            [.. schemas.GetProperty(schema).GetProperty("properties").EnumerateObject().Select(field => field.Name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant())];
        Assert.Equal(["total", "offset", "limit", "games"], Fields("LibraryResponse"));
        Assert.Equal(["gameid", "name", "playtimeseconds", "recentplaytimeseconds", "lastplayedat", "entries", "coversource"], Fields("LibraryGameResponse"));
        Assert.Equal(
            ["storeentryid", "linkedaccountid", "source", "externalid", "playtimeseconds", "recentplaytimeseconds", "lastplayedat", "firstseenat", "asof", "missingsince"],
            Fields("LibraryEntryResponse"));

        // And how to ask for a part of it, in an order.
        var parameters = document.GetProperty("paths").GetProperty("/api/v1/library").GetProperty("get").GetProperty("parameters");
        Assert.Equal(["sort", "offset", "limit"], parameters.EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString()!).ToArray());
        Assert.True(responses.TryGetProperty("422", out _));
    }

    [Fact]
    public async Task What_was_played_lately_is_the_latest_readings_word()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");

        // On the first day already: the store counts its own two weeks.
        await ReadAsync(app, account.Id, Day(1), complete: false, ("620", "Portal 2", 90, 45), ("400", "Portal", 30, null));
        var library = await LibraryAsync(browser);
        Assert.Equal(JsonValueKind.Null, library[0].GetProperty("recent_playtime_seconds").ValueKind);
        Assert.Equal(2700L, library[1].GetProperty("recent_playtime_seconds").GetInt64());
        Assert.Equal(2700L, library[1].GetProperty("entries")[0].GetProperty("recent_playtime_seconds").GetInt64());

        // A window that moves is not carried over: the next reading does not say, so nothing is known.
        var silent = await ReadAsync(app, account.Id, Day(2), complete: false, ("620", "Portal 2", 90, null));
        Assert.Equal(JsonValueKind.Null, (await LibraryAsync(browser))[1].GetProperty("recent_playtime_seconds").ValueKind);

        // The history keeps what each reading said.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var portal2 = (await db.StoreEntries.AsNoTracking().SingleAsync(e => e.ExternalId == "620")).Id;
        var said = await db.Observations.AsNoTracking().Where(o => o.StoreEntryId == portal2).ToDictionaryAsync(o => o.SnapshotId, o => o.RecentPlaytimeSeconds);
        Assert.Equal([null, 2700L], said.Values.Order().ToArray());
        Assert.Null(said[silent]);

        await Assert.ThrowsAsync<ArgumentException>(() => ReadAsync(app, account.Id, Day(3), complete: false, ("620", "Portal 2", 90, -1)));
    }

    [Fact]
    public async Task A_game_a_complete_reading_leaves_out_is_flagged_and_stays_until_its_owner_says()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");
        await ReadAsync(app, account.Id, Day(1), complete: true, ("620", "Portal 2", 90, 10), ("400", "Portal", 30, 5), ("70", "Half-Life", 0, null));

        // A reading that does not vouch for being whole leaves out a game: nothing is concluded.
        await ReadAsync(app, account.Id, Day(2), complete: false, ("620", "Portal 2", 90, null));
        Assert.Equal(["Half-Life", "Portal", "Portal 2"], await NamesAsync(browser));

        // An answer that lists nothing is a profile made private, never an empty library, whatever
        // the connector says of it.
        var empty = await ReadAsync(app, account.Id, Day(3), complete: true);
        Assert.Equal(["Half-Life", "Portal", "Portal 2"], await NamesAsync(browser));

        // A complete one without Portal: refunded, or an answer cut short that still agrees with
        // itself. Nothing here can tell which: the game stays, flagged as missing since that reading.
        await ReadAsync(app, account.Id, Day(4), complete: true, ("620", "Portal 2", 90, null), ("70", "Half-Life", 0, null));
        Assert.Equal(["Half-Life", "Portal", "Portal 2"], await NamesAsync(browser));
        Assert.Equal(new DateTimeOffset?[] { null, Day(4), null }, await MissingAsync(browser));
        Assert.Equal(3, (await browser.GetAsync("/api/v1/library").JsonAsync()).GetProperty("total").GetInt32());

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var portal = (await db.StoreEntries.AsNoTracking().SingleAsync(e => e.ExternalId == "400")).Id;
        var missing = await db.Ownerships.AsNoTracking().SingleAsync(o => o.StoreEntryId == portal);
        Assert.Equal(
            (Day(4), null, 1800L, Day(1), Day(1)),
            (missing.MissingSince, missing.UnlistedSince, missing.PlaytimeSeconds, missing.FirstSeenAt, missing.AsOf));
        // What was played lately was the word of a reading that listed it: not carried over.
        Assert.Null(missing.RecentPlaytimeSeconds);
        Assert.Equal(1, await db.Observations.CountAsync(o => o.StoreEntryId == portal));
        Assert.Equal(
            [(Day(1), true), (Day(2), false), (Day(3), false), (Day(4), true)],
            (await db.RawSnapshots.AsNoTracking().OrderBy(s => s.TakenAt).ToListAsync()).Select(s => (s.TakenAt, s.Complete)).ToArray());
        Assert.Equal(Day(3), (await db.RawSnapshots.AsNoTracking().SingleAsync(s => s.Id == empty)).TakenAt);

        // However many readings leave it out, it stays: no reading takes a game out of a library.
        await ReadAsync(app, account.Id, Day(5), complete: true, ("620", "Portal 2", 90, null), ("70", "Half-Life", 0, null));
        await ReadAsync(app, account.Id, Day(6), complete: true, ("620", "Portal 2", 90, null), ("70", "Half-Life", 0, null));
        Assert.Equal(["Half-Life", "Portal", "Portal 2"], await NamesAsync(browser));
        Assert.Equal(new DateTimeOffset?[] { null, Day(4), null }, await MissingAsync(browser));

        // An older complete reading arriving late left it out already: missing since the earliest.
        await ReadAsync(app, account.Id, Day(1).AddHours(1), complete: true, ("620", "Portal 2", 90, null));
        Assert.Equal(new DateTimeOffset?[] { null, Day(1).AddHours(1), null }, await MissingAsync(browser));

        // A late reading from before that, which listed it, does not undo what is known since.
        await ReadAsync(app, account.Id, Day(1).AddMinutes(30), complete: false, ("400", "Portal", 35, 5));
        var late = await db.Ownerships.AsNoTracking().SingleAsync(o => o.StoreEntryId == portal);
        Assert.Equal((Day(1).AddHours(1), 2100L, Day(1).AddMinutes(30)), (late.MissingSince, late.PlaytimeSeconds, late.AsOf));
        Assert.Null(late.RecentPlaytimeSeconds);

        // Listed again: no longer missing.
        await ReadAsync(app, account.Id, Day(7), complete: true, ("620", "Portal 2", 90, null), ("400", "Portal", 45, null), ("70", "Half-Life", 0, null));
        Assert.Equal(new DateTimeOffset?[] { null, null, null }, await MissingAsync(browser));
        var back = await db.Ownerships.AsNoTracking().SingleAsync(o => o.StoreEntryId == portal);
        Assert.Equal((null, null, 2700L, Day(1), Day(7)), (back.MissingSince, back.UnlistedSince, back.PlaytimeSeconds, back.FirstSeenAt, back.AsOf));
    }

    [Fact]
    public async Task A_missing_game_is_taken_out_by_its_owner_and_stays_out_whatever_its_store_says()
    {
        // The owner decides after every reading of the test.
        var clock = new LibraryClock();
        clock.Set(Day(20));
        await using var app = await StartAsync(clock);
        using var browser = app.Browser();
        using var nobody = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");
        await ReadAsync(app, account.Id, Day(1), complete: true, ("620", "Portal 2", 90, null), ("400", "Portal", 30, 5));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var entry = await db.StoreEntries.AsNoTracking().SingleAsync(e => e.ExternalId == "400");
        var (portal, game, notice) = (entry.Id, $"/api/v1/library/games/{entry.GameId}", $"/api/v1/library/removed/{entry.GameId}/notice");
        Task<OwnershipRow> RowAsync() => db.Ownerships.AsNoTracking().SingleAsync(o => o.StoreEntryId == portal);

        // Every route needs a session.
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await nobody.DeleteAsync(game).RefusalAsync());
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await nobody.PutAsync(game, null).RefusalAsync());
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await nobody.DeleteAsync(notice).RefusalAsync());
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await nobody.GetAsync("/api/v1/library/removed").RefusalAsync());

        // Only a game the library has, and its store no longer lists, is taken out.
        Assert.Equal(
            (HttpStatusCode.NotFound, "not_in_library"),
            await browser.DeleteAsync($"/api/v1/library/games/{Guid.CreateVersion7()}").RefusalAsync());
        Assert.Equal((HttpStatusCode.Conflict, "still_listed"), await browser.DeleteAsync(game).RefusalAsync());
        Assert.Equal((HttpStatusCode.NotFound, "not_removed"), await browser.PutAsync(game, null).RefusalAsync());
        Assert.Equal((HttpStatusCode.NotFound, "not_removed"), await browser.DeleteAsync(notice).RefusalAsync());

        // By its owner alone: another account has no such game in its library.
        await ReadAsync(app, account.Id, Day(2), complete: true, ("620", "Portal 2", 90, null));
        TestDatabase.OpenRegistration();
        using var other = app.Browser();
        await other.RegisterAsync("second@example.org");
        Assert.Equal((HttpStatusCode.NotFound, "not_in_library"), await other.DeleteAsync(game).RefusalAsync());
        Assert.Equal(["Portal", "Portal 2"], await NamesAsync(browser));

        // Taken out an hour after the reading that left it out, before the readings that follow.
        clock.Set(Day(2).AddHours(1));
        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync(game).StatusAsync());
        clock.Set(Day(20));
        Assert.Equal(["Portal 2"], await NamesAsync(browser));
        Assert.Equal((1, 0), ((await StatsAsync(browser)).Games, (await StatsAsync(browser)).Missing));
        Assert.Equal((HttpStatusCode.NotFound, "not_in_library"), await browser.DeleteAsync(game).RefusalAsync());

        // Said where the games taken out are listed, and on the game itself; nobody else's.
        var removed = Assert.Single(await RemovedAsync(browser));
        Assert.Equal((entry.GameId, "Portal", null), (removed.GetProperty("game_id").GetGuid(), removed.GetProperty("name").GetString(), Date(removed, "back_since")));
        var taken = Assert.Single(removed.GetProperty("entries").EnumerateArray());
        Assert.Equal(
            (portal, account.Id, "steam", "400", null, null),
            (taken.GetProperty("store_entry_id").GetGuid(), taken.GetProperty("linked_account_id").GetGuid(), taken.GetProperty("source").GetString(), taken.GetProperty("external_id").GetString(), Date(taken, "listed_again_since"), Date(taken, "notice_dismissed_at")));
        Assert.InRange(Date(taken, "removed_at")!.Value, Day(2), Day(3));
        var seen = await browser.GetAsync($"/api/v1/games/{entry.GameId}").JsonAsync();
        Assert.Empty(seen.GetProperty("entries").EnumerateArray());
        Assert.Equal(entry.GameId, seen.GetProperty("removed").GetProperty("game_id").GetGuid());
        Assert.Empty(await RemovedAsync(other));
        Assert.Equal(JsonValueKind.Null, (await other.GetAsync($"/api/v1/games/{entry.GameId}").JsonAsync()).GetProperty("removed").ValueKind);

        // No store lists it again: no notice to dismiss. A complete reading that leaves it out
        // changes nothing of that.
        Assert.Equal((HttpStatusCode.Conflict, "not_back"), await browser.DeleteAsync(notice).RefusalAsync());
        await ReadAsync(app, account.Id, Day(3), complete: true, ("620", "Portal 2", 90, null));
        Assert.Equal((Day(2), null), ((await RowAsync()).MissingSince, (await RowAsync()).ListedAgainSince));

        // Its store lists it again: what it says is taken, the game stays out, and the API says it
        // is back, since the earliest reading that listed it.
        await ReadAsync(app, account.Id, Day(4), complete: true, ("620", "Portal 2", 90, null), ("400", "Portal", 45, 5));
        await ReadAsync(app, account.Id, Day(5), complete: true, ("620", "Portal 2", 90, null), ("400", "Portal", 45, null));
        Assert.Equal(["Portal 2"], await NamesAsync(browser));
        var back = await RowAsync();
        Assert.NotNull(back.UnlistedSince);
        Assert.Equal((null, Day(4), 2700L, Day(5)), (back.MissingSince, back.ListedAgainSince, back.PlaytimeSeconds, back.AsOf));
        Assert.Null(back.RecentPlaytimeSeconds);
        Assert.Equal(Day(4), Date(Assert.Single(await RemovedAsync(browser)), "back_since"));

        // Dismissed, for good: the game is still listed as taken out, with nothing to tell.
        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync(notice).StatusAsync());
        var dismissed = (await RowAsync()).NoticeDismissedAt;
        Assert.InRange(dismissed!.Value, Day(20), Day(21));
        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync(notice).StatusAsync());
        Assert.Equal(dismissed, (await RowAsync()).NoticeDismissedAt);
        removed = Assert.Single(await RemovedAsync(browser));
        Assert.Null(Date(removed, "back_since"));
        Assert.Equal((Day(4), dismissed), (Date(removed.GetProperty("entries")[0], "listed_again_since"), Date(removed.GetProperty("entries")[0], "notice_dismissed_at")));

        // Left out, then listed once more: no longer back, then back since that reading, and still
        // no notice.
        await ReadAsync(app, account.Id, Day(6), complete: true, ("620", "Portal 2", 90, null));
        Assert.Equal((Day(6), null), ((await RowAsync()).MissingSince, (await RowAsync()).ListedAgainSince));
        Assert.Equal((HttpStatusCode.Conflict, "not_back"), await browser.DeleteAsync(notice).RefusalAsync());
        await ReadAsync(app, account.Id, Day(7), complete: true, ("620", "Portal 2", 90, null), ("400", "Portal", 45, null));
        removed = Assert.Single(await RemovedAsync(browser));
        Assert.Equal((null, Day(7)), (Date(removed, "back_since"), Date(removed.GetProperty("entries")[0], "listed_again_since")));

        // Put back by its owner: in the library as its store last said it, with its first date.
        Assert.Equal(HttpStatusCode.NoContent, await browser.PutAsync(game, null).StatusAsync());
        Assert.Equal(["Portal", "Portal 2"], await NamesAsync(browser));
        Assert.Empty(await RemovedAsync(browser));
        var again = await RowAsync();
        Assert.Equal(
            (null, null, null, null, 2700L, Day(1)),
            (again.UnlistedSince, again.MissingSince, again.ListedAgainSince, again.NoticeDismissedAt, again.PlaytimeSeconds, again.FirstSeenAt));
        Assert.Equal((HttpStatusCode.NotFound, "not_removed"), await browser.PutAsync(game, null).RefusalAsync());

        // Taken out again, then put back while its store does not list it: back, flagged as missing.
        await ReadAsync(app, account.Id, Day(8), complete: true, ("620", "Portal 2", 90, null));
        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync(game).StatusAsync());
        Assert.Null(Date(Assert.Single(await RemovedAsync(browser)), "back_since"));
        Assert.Equal(HttpStatusCode.NoContent, await browser.PutAsync(game, null).StatusAsync());
        Assert.Equal(new DateTimeOffset?[] { Day(8), null }, await MissingAsync(browser));
    }

    [Fact]
    public async Task A_game_owned_twice_loses_only_the_entry_its_store_no_longer_lists()
    {
        var clock = new LibraryClock();
        clock.Set(Day(20));
        await using var app = await StartAsync(clock);
        using var browser = app.Browser();
        var owner = await OwnerAsync(browser);
        var first = await LinkAsync(app, owner, "76561190000000001");
        var second = await LinkAsync(app, owner, "76561190000000002");
        await ReadAsync(app, first.Id, Day(1), complete: true, ("620", "Portal 2", 90, null), ("400", "Portal", 30, null));
        await ReadAsync(app, second.Id, Day(1), complete: true, ("620", "Portal 2", 20, null), ("400", "Portal", 10, null));
        await ReadAsync(app, first.Id, Day(2), complete: true, ("620", "Portal 2", 90, null));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var game = (await db.StoreEntries.AsNoTracking().SingleAsync(e => e.ExternalId == "400")).GameId;

        // The first account's entry goes; the game stays through the second, with its time alone.
        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync($"/api/v1/library/games/{game}").StatusAsync());
        var seen = await browser.GetAsync($"/api/v1/games/{game}").JsonAsync();
        Assert.Equal(second.Id, Assert.Single(seen.GetProperty("entries").EnumerateArray()).GetProperty("linked_account_id").GetGuid());
        Assert.Equal(600L, seen.GetProperty("playtime_seconds").GetInt64());
        Assert.Equal(first.Id, Assert.Single(seen.GetProperty("removed").GetProperty("entries").EnumerateArray()).GetProperty("linked_account_id").GetGuid());

        // What is left is listed by its store: nothing more to take out.
        Assert.Equal((HttpStatusCode.Conflict, "still_listed"), await browser.DeleteAsync($"/api/v1/library/games/{game}").RefusalAsync());

        // Unlinking the account forgets what was taken out of it with the rest.
        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync($"/api/v1/linked-accounts/{first.Id}").StatusAsync());
        Assert.Empty(await RemovedAsync(browser));
    }

    [Fact]
    public async Task A_notice_is_the_games_whichever_entry_is_listed_again_and_an_old_reading_raises_none()
    {
        var clock = new LibraryClock();
        clock.Set(Day(10));
        await using var app = await StartAsync(clock);
        using var browser = app.Browser();
        var owner = await OwnerAsync(browser);
        var first = await LinkAsync(app, owner, "76561190000000001");
        var second = await LinkAsync(app, owner, "76561190000000002");
        foreach (var account in new[] { first, second })
        {
            await ReadAsync(app, account.Id, Day(1), complete: true, ("620", "Portal 2", 90, null), ("400", "Portal", 30, null));
            await ReadAsync(app, account.Id, Day(2), complete: true, ("620", "Portal 2", 90, null));
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var game = (await db.StoreEntries.AsNoTracking().SingleAsync(e => e.ExternalId == "400")).GameId;
        async Task<DateTimeOffset?> BackSinceAsync() => Date(Assert.Single(await RemovedAsync(browser)), "back_since");

        // Both entries are missing: both go, on the tenth day.
        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync($"/api/v1/library/games/{game}").StatusAsync());
        Assert.Equal(2, Assert.Single(await RemovedAsync(browser)).GetProperty("entries").GetArrayLength());
        clock.Set(Day(20));

        // A reading from before that day, arriving late, lists it: nothing new to tell its owner.
        await ReadAsync(app, first.Id, Day(3), complete: false, ("400", "Portal", 35, null));
        Assert.Null(await BackSinceAsync());

        // One taken since does, whole library or not; one that says nothing of the game changes nothing.
        await ReadAsync(app, first.Id, Day(11), complete: false, ("400", "Portal", 40, null));
        await ReadAsync(app, first.Id, Day(12), complete: false, ("620", "Portal 2", 90, null));
        Assert.Equal(Day(11), await BackSinceAsync());

        // Dismissed for the game: the other entry, listed later by its own store, raises none.
        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync($"/api/v1/library/removed/{game}/notice").StatusAsync());
        Assert.All(
            Assert.Single(await RemovedAsync(browser)).GetProperty("entries").EnumerateArray(),
            entry => Assert.NotNull(Date(entry, "notice_dismissed_at")));
        await ReadAsync(app, second.Id, Day(13), complete: true, ("620", "Portal 2", 90, null), ("400", "Portal", 30, null));
        Assert.Null(await BackSinceAsync());
        Assert.Equal(
            new DateTimeOffset?[] { Day(11), Day(13) },
            Assert.Single(await RemovedAsync(browser)).GetProperty("entries").EnumerateArray().Select(entry => Date(entry, "listed_again_since")).Order());
    }

    [Fact]
    public async Task A_dismissal_with_no_notice_shown_silences_nothing_ahead_of_time()
    {
        var clock = new LibraryClock();
        clock.Set(Day(10));
        await using var app = await StartAsync(clock);
        using var browser = app.Browser();
        var owner = await OwnerAsync(browser);
        var first = await LinkAsync(app, owner, "76561190000000001");
        var second = await LinkAsync(app, owner, "76561190000000002");
        foreach (var account in new[] { first, second })
        {
            await ReadAsync(app, account.Id, Day(1), complete: true, ("620", "Portal 2", 90, null), ("400", "Portal", 30, null));
        }

        await ReadAsync(app, first.Id, Day(2), complete: true, ("620", "Portal 2", 90, null));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var game = (await db.StoreEntries.AsNoTracking().SingleAsync(e => e.ExternalId == "400")).GameId;
        var (route, notice) = ($"/api/v1/library/games/{game}", $"/api/v1/library/removed/{game}/notice");
        async Task<JsonElement> RemovedGameAsync() => Assert.Single(await RemovedAsync(browser));
        async Task<DateTimeOffset?> DismissedAsync(Guid account) =>
            Date((await RemovedGameAsync()).GetProperty("entries").EnumerateArray().Single(entry => entry.GetProperty("linked_account_id").GetGuid() == account), "notice_dismissed_at");

        // The first account's entry goes, its store lists it again, and that notice is dismissed.
        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync(route).StatusAsync());
        clock.Set(Day(20));
        await ReadAsync(app, first.Id, Day(11), complete: true, ("620", "Portal 2", 90, null), ("400", "Portal", 30, null));
        Assert.Equal(Day(11), Date(await RemovedGameAsync(), "back_since"));
        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync(notice).StatusAsync());

        // The second account's entry goes later, while its store does not list it: no notice to show.
        await ReadAsync(app, second.Id, Day(12), complete: true, ("620", "Portal 2", 90, null));
        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync(route).StatusAsync());
        Assert.Null(Date(await RemovedGameAsync(), "back_since"));

        // Dismissing again changes nothing: the entry that is not back is not silenced ahead of time.
        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync(notice).StatusAsync());
        Assert.NotNull(await DismissedAsync(first.Id));
        Assert.Null(await DismissedAsync(second.Id));

        // So when its store lists it again, the notice is raised.
        clock.Set(Day(25));
        await ReadAsync(app, second.Id, Day(21), complete: true, ("620", "Portal 2", 90, null), ("400", "Portal", 30, null));
        Assert.Equal(Day(21), Date(await RemovedGameAsync(), "back_since"));
    }

    [Fact]
    public async Task The_library_is_one_item_per_game_read_in_parts_and_in_an_order()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var owner = await OwnerAsync(browser);
        var account = await LinkAsync(app, owner, "76561190000000001");
        var other = await LinkAsync(app, owner, "76561190000000002");
        await ReadAsync(app, account.Id, Day(1), complete: true, ("620", "Portal 2", 90, 30), ("621", "portal 2 (beta)", 10, null), ("400", "Élan", 600, null), ("70", null, null, null));
        await RecordAsync(app, account.Id, Day(2), "{}", ("620", "Portal 2", 90, LastPlayed), ("400", "Élan", 600, LastPlayed.AddDays(-1)));
        // The same game through a second account of the same store.
        await ReadAsync(app, other.Id, Day(2), complete: true, ("620", "Portal 2", 20, 20), ("10", "zebra", 5, null));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var entries = await db.StoreEntries.AsNoTracking().ToDictionaryAsync(e => e.ExternalId);
        // A merge: the beta's entry now points to Portal 2's game.
        await db.StoreEntries.Where(e => e.ExternalId == "621").ExecuteUpdateAsync(entry => entry.SetProperty(e => e.GameId, entries["620"].GameId));

        // One item per game: its entries under it, its totals added up, its last session the latest.
        var whole = await browser.GetAsync("/api/v1/library").JsonAsync();
        Assert.Equal((4, 0, 100), (whole.GetProperty("total").GetInt32(), whole.GetProperty("offset").GetInt32(), whole.GetProperty("limit").GetInt32()));
        // By name: letters before case and accents, the game without a name last.
        Assert.Equal(["Élan", "Portal 2", "zebra", "(none)"], await NamesAsync(browser));
        var portal2 = whole.GetProperty("games")[1];
        Assert.Equal(entries["620"].GameId, portal2.GetProperty("game_id").GetGuid());
        Assert.Equal((120 * 60L, 20 * 60L, LastPlayed), (portal2.GetProperty("playtime_seconds").GetInt64(), portal2.GetProperty("recent_playtime_seconds").GetInt64(), portal2.GetProperty("last_played_at").GetDateTimeOffset()));
        var owned = portal2.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(
            new[] { ("620", account.Id, 5400L), ("620", other.Id, 1200L), ("621", account.Id, 600L) }.Order(),
            owned.Select(entry => (entry.GetProperty("external_id").GetString()!, entry.GetProperty("linked_account_id").GetGuid(), entry.GetProperty("playtime_seconds").GetInt64())).Order());
        Assert.Equal(3, owned.Select(entry => (entry.GetProperty("store_entry_id").GetGuid(), entry.GetProperty("linked_account_id").GetGuid())).Distinct().Count());
        var first = owned.Single(entry => entry.GetProperty("external_id").GetString() == "620" && entry.GetProperty("linked_account_id").GetGuid() == account.Id);
        Assert.Equal((Day(1), Day(2)), (first.GetProperty("first_seen_at").GetDateTimeOffset(), first.GetProperty("as_of").GetDateTimeOffset()));
        Assert.All(owned, entry => Assert.Equal(Store, entry.GetProperty("source").GetString()));

        // The other orders: what they cannot place comes last, by name.
        async Task<string[]> Names(string query) =>
            [.. (await browser.GetAsync("/api/v1/library" + query).JsonAsync()).GetProperty("games").EnumerateArray().Select(game => game.GetProperty("name").GetString() ?? "(none)")];
        Assert.Equal(["Élan", "Portal 2", "zebra", "(none)"], await Names("?sort=playtime"));
        Assert.Equal(["Portal 2", "Élan", "zebra", "(none)"], await Names("?sort=last_played"));

        // In parts: the total stays the whole library's, and the parts follow each other.
        var part = await browser.GetAsync("/api/v1/library?sort=last_played&offset=1&limit=2").JsonAsync();
        Assert.Equal((4, 1, 2), (part.GetProperty("total").GetInt32(), part.GetProperty("offset").GetInt32(), part.GetProperty("limit").GetInt32()));
        Assert.Equal(["Élan", "zebra"], await Names("?sort=last_played&offset=1&limit=2"));
        Assert.Empty(await Names("?offset=4"));

        // What cannot be asked is refused in the one error format.
        foreach (var query in new[] { "?limit=0", "?limit=501", "?offset=-1", "?sort=rating", "?limit=many" })
        {
            Assert.Equal((HttpStatusCode.UnprocessableEntity, "invalid_request"), await browser.GetAsync("/api/v1/library" + query).RefusalAsync());
        }
    }

    [Fact]
    public async Task A_reading_becomes_a_snapshot_the_catalogue_and_a_library()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var owner = await OwnerAsync(browser);
        var account = await LinkAsync(app, owner, "76561190000000001");

        var snapshot = await RecordAsync(app, account.Id, Day(1), Payload, ("620", "Portal 2", 90, LastPlayed), ("400", "Portal", 0, null));

        // The answer, untouched, dated when the source answered, with the account it was read from.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var stored = await db.RawSnapshots.SingleAsync();
        Assert.Equal(Payload, stored.Payload);
        Assert.Equal((snapshot, Store, "owned-games", Day(1)), (stored.Id, stored.Source, stored.Kind, stored.TakenAt));
        Assert.Equal(account.Id, stored.LinkedAccountId);
        Assert.InRange(stored.RecordedAt, Today, Today.AddMinutes(1));

        // One canonical record per new store entry, and each entry points to its own.
        var entries = await db.StoreEntries.OrderBy(e => e.ExternalId).ToListAsync();
        Assert.Equal([("400", "Portal"), ("620", "Portal 2")], entries.Select(e => (e.ExternalId, e.Name!)).ToArray());
        Assert.All(entries, entry => Assert.Equal(Store, entry.Source));
        var games = await db.Games.ToDictionaryAsync(g => g.Id);
        Assert.Equal(2, games.Count);
        Assert.All(entries, entry => Assert.Equal(entry.Name, games[entry.GameId].Name));
        Assert.All(entries, entry => Assert.Equal(entry.Id, games[entry.GameId].NameSourceEntryId));
        Assert.All(games.Values, game => Assert.Null(game.IgdbId));

        // Ownership and playtime, per store entry, traced to the snapshot.
        var ownerships = await db.Ownerships.ToDictionaryAsync(o => o.StoreEntryId);
        Assert.Equal(2, ownerships.Count);
        var played = ownerships[entries[1].Id];
        Assert.Equal((account.Id, 90 * 60L, snapshot), (played.LinkedAccountId, played.PlaytimeSeconds!.Value, played.SnapshotId));
        Assert.Equal((LastPlayed, Day(1), Day(1)), (played.LastPlayedAt!.Value, played.FirstSeenAt, played.AsOf));
        Assert.Equal(0L, ownerships[entries[0].Id].PlaytimeSeconds);
        Assert.Null(ownerships[entries[0].Id].LastPlayedAt);

        // What the snapshot said, one row per game.
        var said = await db.Observations.Where(o => o.SnapshotId == snapshot).ToDictionaryAsync(o => o.StoreEntryId, o => o.PlaytimeSeconds);
        Assert.Equal((2, 0L, 5400L), (said.Count, said[entries[0].Id]!.Value, said[entries[1].Id]!.Value));

        // And the route, sorted by name.
        var library = await LibraryAsync(browser);
        Assert.Equal(["Portal", "Portal 2"], await NamesAsync(browser));
        var second = library[1];
        Assert.Equal(games.Values.Single(g => g.Name == "Portal 2").Id, second.GetProperty("game_id").GetGuid());
        var owned = Assert.Single(second.GetProperty("entries").EnumerateArray());
        Assert.Equal((Store, "620", 5400L), (owned.GetProperty("source").GetString(), owned.GetProperty("external_id").GetString(), second.GetProperty("playtime_seconds").GetInt64()));
        Assert.Equal(LastPlayed, second.GetProperty("last_played_at").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, library[0].GetProperty("last_played_at").ValueKind);
    }

    [Fact]
    public async Task Two_readings_give_two_snapshots_and_a_playtime_difference()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");
        var first = await RecordAsync(app, account.Id, Day(1), "{\"day\":1}", ("620", "Portal 2", 90, null), ("400", "Portal", 30, null));

        // The next day: one game played, one bought, and one the store no longer lists.
        var second = await RecordAsync(app, account.Id, Day(2), "{\"day\":2}", ("620", "Portal 2", 150, LastPlayed), ("70", "Half-Life", 0, null));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Equal(["{\"day\":1}", "{\"day\":2}"], await db.RawSnapshots.OrderBy(s => s.TakenAt).Select(s => s.Payload).ToArrayAsync());
        Assert.Equal(3, await db.StoreEntries.CountAsync());
        Assert.Equal(3, await db.Games.CountAsync());

        // The history is a query: what each snapshot said of a game, by the date the source answered.
        var history = await (
            from observation in db.Observations
            join snapshot in db.RawSnapshots on observation.SnapshotId equals snapshot.Id
            join entry in db.StoreEntries on observation.StoreEntryId equals entry.Id
            where snapshot.LinkedAccountId == account.Id && entry.ExternalId == "620"
            orderby snapshot.TakenAt
            select new { snapshot.TakenAt, observation.KnownPlaytimeSeconds })
            .ToListAsync();
        Assert.Equal([(Day(1), 5400L), (Day(2), 9000L)], history.Select(h => (h.TakenAt, h.KnownPlaytimeSeconds!.Value)).ToArray());
        Assert.Equal(TimeSpan.FromHours(1), TimeSpan.FromSeconds(history[1].KnownPlaytimeSeconds!.Value - history[0].KnownPlaytimeSeconds!.Value));
        Assert.Equal(4, await db.Observations.CountAsync());

        // The library is the latest state.
        var ownerships = await (
            from ownership in db.Ownerships
            join entry in db.StoreEntries on ownership.StoreEntryId equals entry.Id
            select new { entry.ExternalId, ownership.PlaytimeSeconds, ownership.SnapshotId, ownership.FirstSeenAt, ownership.AsOf })
            .ToDictionaryAsync(o => o.ExternalId);
        Assert.Equal((150 * 60L, second, Day(1), Day(2)), (ownerships["620"].PlaytimeSeconds!.Value, ownerships["620"].SnapshotId, ownerships["620"].FirstSeenAt, ownerships["620"].AsOf));
        Assert.Equal((0L, second, Day(2), Day(2)), (ownerships["70"].PlaytimeSeconds!.Value, ownerships["70"].SnapshotId, ownerships["70"].FirstSeenAt, ownerships["70"].AsOf));
        // Not listed any more: left as the first reading said.
        Assert.Equal((30 * 60L, first, Day(1)), (ownerships["400"].PlaytimeSeconds!.Value, ownerships["400"].SnapshotId, ownerships["400"].AsOf));

        Assert.Equal([("Half-Life", 0L), ("Portal", 1800L), ("Portal 2", 9000L)], await PlaytimesAsync(browser));
    }

    [Fact]
    public async Task A_reading_is_dated_when_the_source_answered()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");

        // Answered just before midnight, somewhere east of Greenwich, and handed over days later.
        var answered = new DateTimeOffset(2026, 11, 2, 23, 59, 58, TimeSpan.FromHours(1));
        var played = new DateTimeOffset(2026, 11, 2, 21, 0, 0, TimeSpan.FromHours(1));
        var late = await RecordAsync(app, account.Id, answered, "{}", ("620", "Portal 2", 150, played));
        // A reading older still arrives after it: a retry, an import of the past.
        var older = await RecordAsync(app, account.Id, Day(1), "{}", ("620", "An older name", 90, null), ("400", "Portal", 30, null));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var snapshots = await db.RawSnapshots.ToDictionaryAsync(s => s.Id);
        Assert.Equal((answered, TimeSpan.Zero), (snapshots[late].TakenAt, snapshots[late].TakenAt.Offset));
        Assert.Equal(Day(1), snapshots[older].TakenAt);
        Assert.All(snapshots.Values, snapshot => Assert.InRange(snapshot.RecordedAt, Today, Today.AddMinutes(1)));

        // Both are history, each at its date.
        Assert.Equal(3, await db.Observations.CountAsync());

        // What is known stays what the most recent answer said; the game was only owned earlier than thought.
        var portal2 = await db.Ownerships.SingleAsync(o => o.SnapshotId == late);
        Assert.Equal((9000L, played, Day(1), answered), (portal2.PlaytimeSeconds!.Value, portal2.LastPlayedAt!.Value, portal2.FirstSeenAt, portal2.AsOf));
        Assert.Equal([("Portal", 1800L), ("Portal 2", 9000L)], await PlaytimesAsync(browser));

        // A reading from the future is a mistake, and would make every later one look old.
        await Assert.ThrowsAsync<ArgumentException>(() => RecordAsync(app, account.Id, Today.AddHours(1), "{}", ("620", "Portal 2", 151, null)));
        Assert.Equal(2, await db.RawSnapshots.CountAsync());
    }

    [Fact]
    public async Task A_poorer_reading_erases_nothing()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");
        await RecordAsync(app, account.Id, Day(1), "{}", ("620", "Portal 2", 90, LastPlayed), ("400", "Portal", 0, null), ("70", null, null, null));

        // The store answers with less: no playtime counted, no date, no name.
        var poorer = await RecordAsync(app, account.Id, Day(2), "{}", ("620", null, 0, null), ("400", " ", 0, null), ("70", null, null, null));

        var library = await LibraryAsync(browser);
        Assert.Equal(["Portal", "Portal 2", "(none)"], await NamesAsync(browser));
        Assert.Equal((5400L, LastPlayed), (library[1].GetProperty("playtime_seconds").GetInt64(), library[1].GetProperty("last_played_at").GetDateTimeOffset()));
        Assert.Equal(0L, library[0].GetProperty("playtime_seconds").GetInt64()); // never played is still zero
        Assert.Equal(JsonValueKind.Null, library[2].GetProperty("playtime_seconds").ValueKind); // never said is not zero

        // The history keeps what was said that day, poor as it was, and the library says it was listed.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var said = await db.Observations.Where(o => o.SnapshotId == poorer).ToListAsync();
        Assert.Equal([null, 0L, 0L], said.Select(o => o.PlaytimeSeconds).Order().ToArray());
        Assert.All(said, observation => Assert.Null(observation.LastPlayedAt));
        Assert.All(await db.Ownerships.ToListAsync(), ownership => Assert.Equal((poorer, Day(2)), (ownership.SnapshotId, ownership.AsOf)));

        // Silence again, then the store corrects a total downwards without reaching zero: taken.
        await RecordAsync(app, account.Id, Day(3), "{}", ("620", "Portal 2", null, null));
        Assert.Equal(5400L, (await LibraryAsync(browser))[1].GetProperty("playtime_seconds").GetInt64());
        await RecordAsync(app, account.Id, Day(4), "{}", ("620", "Portal 2", 60, null), ("70", "Half-Life", 5, null));
        Assert.Equal([("Half-Life", 300L), ("Portal", 0L), ("Portal 2", 3600L)], await PlaytimesAsync(browser));
        Assert.Equal(LastPlayed, (await LibraryAsync(browser))[2].GetProperty("last_played_at").GetDateTimeOffset());

        // A history is read from what was known after each reading, not from what each one said: the
        // two poor days count no playtime, lost or regained, and the correction is the only step.
        var portal2 = (await db.StoreEntries.AsNoTracking().SingleAsync(e => e.ExternalId == "620")).Id;
        var history = await (
            from observation in db.Observations.AsNoTracking()
            join snapshot in db.RawSnapshots on observation.SnapshotId equals snapshot.Id
            where observation.StoreEntryId == portal2
            orderby snapshot.TakenAt
            select new { observation.PlaytimeSeconds, observation.KnownPlaytimeSeconds }).ToListAsync();
        Assert.Equal([5400L, 0L, null, 3600L], history.Select(day => day.PlaytimeSeconds).ToArray());
        Assert.Equal([5400L, 5400L, 5400L, 3600L], history.Select(day => day.KnownPlaytimeSeconds).ToArray());
    }

    [Fact]
    public async Task A_store_entry_follows_its_store_and_a_game_keeps_a_name_given_otherwise()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");
        await RecordAsync(app, account.Id, Day(1), "{}", ("620", null, 10, null), ("400", "Portal", 10, null), ("70", "Half-Life", 10, null));
        Assert.Equal(["Half-Life", "Portal", "(none)"], await NamesAsync(browser));

        // Named at last, and renamed by its store: the entry and the game it created follow.
        await RecordAsync(app, account.Id, Day(2), "{}", ("620", "Portal 2", 10, null), ("400", "Portal Classic", 10, null));
        Assert.Equal(["Half-Life", "Portal 2", "Portal Classic"], await NamesAsync(browser));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var entries = await db.StoreEntries.AsNoTracking().ToDictionaryAsync(e => e.ExternalId);

        // A game named by hand: the next readings leave its name alone, and the entry still follows.
        await db.Games.Where(g => g.Id == entries["620"].GameId)
            .ExecuteUpdateAsync(game => game.SetProperty(g => g.Name, "Portal Two").SetProperty(g => g.NameSourceEntryId, (Guid?)null));
        // A merge: Half-Life's entry now points to the game that Portal's entry created and named.
        await db.StoreEntries.Where(e => e.ExternalId == "70").ExecuteUpdateAsync(entry => entry.SetProperty(e => e.GameId, entries["400"].GameId));

        await RecordAsync(app, account.Id, Day(3), "{}", ("620", "Portal 2 Remastered", 10, null), ("70", "Half-Life: Source", 10, null));

        var names = await db.StoreEntries.AsNoTracking().ToDictionaryAsync(e => e.ExternalId, e => e.Name);
        Assert.Equal(("Portal 2 Remastered", "Half-Life: Source", "Portal Classic"), (names["620"], names["70"], names["400"]));
        // The merged game is in the library once, with its two entries.
        Assert.Equal(["Portal Classic", "Portal Two"], await NamesAsync(browser));
        Assert.Equal(2, (await LibraryAsync(browser))[0].GetProperty("entries").GetArrayLength());

        // The entry a merged game took its name from still renames it.
        await RecordAsync(app, account.Id, Day(4), "{}", ("400", "Portal", 10, null));
        Assert.Equal(["Portal", "Portal Two"], await NamesAsync(browser));
    }

    [Fact]
    public async Task A_merged_game_follows_the_entry_that_named_it_even_when_both_bore_the_same_name()
    {
        // The usual duplicate: one game, known to two entries under the same name.
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");
        await RecordAsync(app, account.Id, Day(1), "{}", ("620", "Portal 2", 10, null), ("621", "Portal 2", 10, null));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var entries = await db.StoreEntries.AsNoTracking().ToDictionaryAsync(e => e.ExternalId);
        await db.StoreEntries.Where(e => e.ExternalId == "621").ExecuteUpdateAsync(entry => entry.SetProperty(e => e.GameId, entries["620"].GameId));

        // The entry merged in is renamed: the game keeps the name of the entry that named it.
        await RecordAsync(app, account.Id, Day(2), "{}", ("621", "Portal 2 - Deluxe", 10, null));
        Assert.Equal("Portal 2", (await db.Games.AsNoTracking().SingleAsync(g => g.Id == entries["620"].GameId)).Name);
        Assert.Equal("Portal 2 - Deluxe", (await db.StoreEntries.AsNoTracking().SingleAsync(e => e.ExternalId == "621")).Name);

        // That entry renamed in its turn: the game follows.
        await RecordAsync(app, account.Id, Day(3), "{}", ("620", "Portal 2 (2011)", 10, null));
        Assert.Equal("Portal 2 (2011)", (await db.Games.AsNoTracking().SingleAsync(g => g.Id == entries["620"].GameId)).Name);
    }

    [Fact]
    public async Task Two_owners_of_a_game_share_its_record_and_nothing_else()
    {
        await using var app = await StartAsync();
        using var first = app.Browser();
        var one = await LinkAsync(app, await OwnerAsync(first), "76561190000000001");
        TestDatabase.OpenRegistration();
        using var second = app.Browser();
        var other = await LinkAsync(app, await OwnerAsync(second, "second@example.org"), "76561190000000002");

        await RecordAsync(app, one.Id, Day(1), "{}", ("620", "Portal 2", 90, null), ("400", "Portal", 10, null));
        await RecordAsync(app, other.Id, Day(1), "{}", ("620", "Portal 2", 600, null));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Equal(2, await db.StoreEntries.CountAsync());
        Assert.Equal(2, await db.Games.CountAsync());
        Assert.Equal(3, await db.Ownerships.CountAsync());

        Assert.Equal([("Portal 2", 36000L)], await PlaytimesAsync(second));
        Assert.Equal([("Portal", 600L), ("Portal 2", 5400L)], await PlaytimesAsync(first));
        Assert.Equal((await LibraryAsync(first))[1].GetProperty("game_id").GetGuid(), (await LibraryAsync(second))[0].GetProperty("game_id").GetGuid());
    }

    [Fact]
    public async Task A_store_account_has_one_owner()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var owner = await OwnerAsync(browser);
        var linked = await LinkAsync(app, owner, "76561190000000001", "  Player One ");
        Assert.Equal((owner, Store, "76561190000000001", "Player One"), (linked.OwnerId, linked.Source, linked.ExternalId, linked.DisplayName));

        // Linked again by its owner: the same link. By someone else: refused.
        Assert.Equal(linked.Id, (await LinkAsync(app, owner, "76561190000000001", "Renamed")).Id);
        var refused = await Assert.ThrowsAsync<GamesException>(() => LinkAsync(app, Guid.CreateVersion7(), "76561190000000001"));
        Assert.Equal(GamesError.LinkedToAnotherAccount, refused.Error);

        // The same id in another store is another account, and an owner may link several.
        var elsewhere = await UseAsync(app, library => library.LinkAccountAsync(owner, "gog", "76561190000000001", null, default));
        Assert.NotEqual(linked.Id, elsewhere.Id);

        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<GamesDbContext>().LinkedAccounts.CountAsync(a => a.OwnerId == owner));
    }

    [Fact]
    public async Task A_reading_that_cannot_be_recorded_leaves_nothing()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");

        var unknown = await Assert.ThrowsAsync<GamesException>(() => RecordAsync(app, Guid.CreateVersion7(), Day(1), "{}", ("620", "Portal 2", 1, null)));
        Assert.Equal(GamesError.UnknownLinkedAccount, unknown.Error);
        await Assert.ThrowsAsync<ArgumentException>(() => RecordAsync(app, account.Id, Day(1), "{}", (" ", "Portal 2", 1, null)));
        await Assert.ThrowsAsync<ArgumentException>(() => RecordAsync(app, account.Id, Day(1), "{}", ("620", "Portal 2", -1, null)));
        await Assert.ThrowsAsync<ArgumentException>(() => UseAsync(app, library => library.LinkAccountAsync(account.OwnerId, "Steam Store", "1", null, default)));

        // The snapshot and the catalogue are one transaction with the library: a failure half-way keeps none.
        await using var broken = TestApp.Create(services: services => services.AddScoped<ILibraryStore, FailingLibrary>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => RecordAsync(broken, account.Id, Day(1), "{}", ("620", "Portal 2", 1, null)));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Equal((0, 0, 0), (await db.RawSnapshots.CountAsync(), await db.StoreEntries.CountAsync(), await db.Games.CountAsync()));
    }

    [Fact]
    public async Task A_game_listed_twice_is_recorded_once_and_long_names_are_cut()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");

        await RecordAsync(app, account.Id, Day(1), "{}", ("620", "Portal 2", 10, null), ("620", new string('x', 600), 20, null));

        Assert.Equal([(new string('x', 500), 1200L)], await PlaytimesAsync(browser));
    }

    [Fact]
    public async Task The_library_in_figures()
    {
        var clock = new LibraryClock();
        await using var app = await StartAsync(clock);
        using var browser = app.Browser();
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await browser.GetAsync("/api/v1/library/stats").RefusalAsync());
        var owner = await OwnerAsync(browser);

        // Nothing read yet: nothing counted, and no store said any time.
        Assert.Equal((0, 0, 0, 0, null, null, 0, null), await StatsAsync(browser));

        // Two accounts of one store, the same game on both: one game, its times added up.
        var first = await LinkAsync(app, owner, "76561190000000001");
        var second = await LinkAsync(app, owner, "76561190000000002");
        await ReadAsync(app, first.Id, Day(2), complete: true, ("620", "Portal 2", 90, 45), ("400", "Portal", 30, null), ("70", "Half-Life", 0, null));
        await ReadAsync(app, second.Id, Day(1), complete: true, ("620", "Portal 2", 10, 5), ("220", "Half-Life 2", 0, null));
        Assert.Equal((4, 2, 2, 0, 130 * 60L, 50 * 60L, 1, Day(1)), await StatsAsync(browser));
        Assert.Equal(4, (await browser.GetAsync("/api/v1/library").JsonAsync()).GetProperty("total").GetInt32());

        // Two weeks after a reading, what it said was played lately is not recent any more, in the
        // figures as in the library; a younger reading still counts.
        clock.Set(Day(15).AddHours(1));
        Assert.Equal((4, 2, 2, 0, 130 * 60L, 45 * 60L, 1, Day(1)), await StatsAsync(browser));
        var portal2 = (await browser.GetAsync("/api/v1/library").JsonAsync()).GetProperty("games").EnumerateArray()
            .Single(game => game.GetProperty("name").GetString() == "Portal 2");
        Assert.Equal(45 * 60L, portal2.GetProperty("recent_playtime_seconds").GetInt64());
        Assert.Equal(
            [45 * 60L, null],
            portal2.GetProperty("entries").EnumerateArray()
                .Select(entry => entry.GetProperty("recent_playtime_seconds") is { ValueKind: JsonValueKind.Number } recent ? recent.GetInt64() : (long?)null)
                .OrderDescending());
        clock.Set(Day(16).AddHours(1));
        Assert.Equal((4, 2, 2, 0, 130 * 60L, null, 0, Day(1)), await StatsAsync(browser));

        // A game the store leaves out stays, flagged, and is counted as missing. Missing from one
        // account only, a game is still owned through the other.
        await ReadAsync(app, first.Id, Day(3), complete: true, ("620", "Portal 2", 90, null), ("70", "Half-Life", 0, null));
        await ReadAsync(app, second.Id, Day(3), complete: true, ("220", "Half-Life 2", 0, null));
        Assert.Equal((4, 2, 2, 1, 130 * 60L, null, 0, Day(1)), await StatsAsync(browser));

        // Someone else's library counts for nothing here.
        TestDatabase.OpenRegistration();
        using var other = app.Browser();
        await OwnerAsync(other, "second@example.org");
        Assert.Equal((0, 0, 0, 0, null, null, 0, null), await StatsAsync(other));
    }

    [Fact]
    public async Task The_description_says_what_the_figures_are()
    {
        await using var app = await StartAsync();
        using var browser = app.Browser();
        var document = await browser.GetAsync("/api/v1/openapi.json").JsonAsync();

        var responses = document.GetProperty("paths").GetProperty("/api/v1/library/stats").GetProperty("get").GetProperty("responses");
        Assert.Equal(["200", "401"], responses.EnumerateObject().Select(response => response.Name).Order());
        var stats = document.GetProperty("components").GetProperty("schemas").GetProperty("LibraryStatsResponse").GetProperty("properties");
        Assert.Equal(
            ["games", "launched", "missing", "never_launched", "playtime_seconds", "recent_playtime_seconds", "recently_played", "tracking_since"],
            stats.EnumerateObject().Select(property => property.Name).Order());
    }

    /// <summary>A day of the past, at the hour a daily reading would be taken.</summary>
    private static DateTimeOffset Day(int day) => new(2026, 11, day, 6, 0, 0, TimeSpan.Zero);

    /// <summary>When the app's clock starts: after every reading of the tests.</summary>
    private static readonly DateTimeOffset Today = Day(8);

    /// <summary>The app, its clock set after the readings of <see cref="Day"/>: what they said was played
    /// lately is still recent, whatever the date the tests run.</summary>
    private static Task<TestApp> StartAsync(LibraryClock? clock = null) =>
        TestApp.StartAsync(services: services => services.AddSingleton<TimeProvider>(clock ?? new LibraryClock()));

    /// <summary>A clock that starts on a day of the tests' and goes on: two links made one after the other
    /// are not made at the same moment.</summary>
    private sealed class LibraryClock : TimeProvider
    {
        private readonly Stopwatch _since = Stopwatch.StartNew();
        private DateTimeOffset _start = Today;

        public override DateTimeOffset GetUtcNow() => _start + _since.Elapsed;

        public void Set(DateTimeOffset now)
        {
            _start = now;
            _since.Restart();
        }
    }

    private static async Task<(int Games, int Launched, int NeverLaunched, int Missing, long? Playtime, long? Recent, int RecentlyPlayed, DateTimeOffset? Since)> StatsAsync(HttpClient browser)
    {
        var stats = await browser.GetAsync("/api/v1/library/stats").JsonAsync();
        long? Long(string name) => stats.GetProperty(name) is { ValueKind: JsonValueKind.Number } value ? value.GetInt64() : null;
        return (
            stats.GetProperty("games").GetInt32(),
            stats.GetProperty("launched").GetInt32(),
            stats.GetProperty("never_launched").GetInt32(),
            stats.GetProperty("missing").GetInt32(),
            Long("playtime_seconds"),
            Long("recent_playtime_seconds"),
            stats.GetProperty("recently_played").GetInt32(),
            stats.GetProperty("tracking_since") is { ValueKind: JsonValueKind.String } since ? since.GetDateTimeOffset() : null);
    }

    private static Task<Guid> RecordAsync(
        TestApp app,
        Guid account,
        DateTimeOffset takenAt,
        string payload,
        params (string Id, string? Name, int? Minutes, DateTimeOffset? LastPlayed)[] items)
    {
        var reading = new LibraryReading(
            "owned-games",
            takenAt,
            payload,
            [.. items.Select(item => new OwnedItem(
                item.Id, item.Name, item.Minutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null, item.LastPlayed))]);
        return UseAsync(app, library => library.RecordLibraryAsync(account, reading, default));
    }

    /// <summary>A reading that says what was played lately, and whether it is the whole library.</summary>
    private static Task<Guid> ReadAsync(
        TestApp app,
        Guid account,
        DateTimeOffset takenAt,
        bool complete,
        params (string Id, string? Name, int? Minutes, int? RecentMinutes)[] items)
    {
        var reading = new LibraryReading(
            "owned-games",
            takenAt,
            "{}",
            [.. items.Select(item => new OwnedItem(
                item.Id,
                item.Name,
                item.Minutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null,
                LastPlayedAt: null,
                item.RecentMinutes is { } recent ? TimeSpan.FromMinutes(recent) : null))],
            complete);
        return UseAsync(app, library => library.RecordLibraryAsync(account, reading, default));
    }

    private static async Task<JsonElement[]> LibraryAsync(HttpClient browser) =>
        [.. (await browser.GetAsync("/api/v1/library").JsonAsync()).GetProperty("games").EnumerateArray()];

    /// <summary>The names in the order of the library; a game without one reads "(none)".</summary>
    private static async Task<string[]> NamesAsync(HttpClient browser) =>
        [.. (await LibraryAsync(browser)).Select(game => game.GetProperty("name").GetString() ?? "(none)")];

    /// <summary>Since when each game's first entry is missing, in the order of the library.</summary>
    private static async Task<DateTimeOffset?[]> MissingAsync(HttpClient browser) =>
        [.. (await LibraryAsync(browser)).Select(game => game.GetProperty("entries")[0].GetProperty("missing_since") is { ValueKind: JsonValueKind.String } since
            ? since.GetDateTimeOffset()
            : (DateTimeOffset?)null)];

    /// <summary>The games taken out of the library, in the order the API gives them.</summary>
    private static async Task<JsonElement[]> RemovedAsync(HttpClient browser) =>
        [.. (await browser.GetAsync("/api/v1/library/removed").JsonAsync()).GetProperty("games").EnumerateArray()];

    /// <summary>A date of an answer, or null when it has none.</summary>
    private static DateTimeOffset? Date(JsonElement item, string name) =>
        item.GetProperty(name) is { ValueKind: JsonValueKind.String } date ? date.GetDateTimeOffset() : null;

    /// <summary>The library as names and seconds, for the games that have both.</summary>
    private static async Task<(string Name, long Seconds)[]> PlaytimesAsync(HttpClient browser) =>
        [.. (await LibraryAsync(browser)).Select(game => (game.GetProperty("name").GetString()!, game.GetProperty("playtime_seconds").GetInt64()))];

    /// <summary>The last step of a recording, failing.</summary>
    private sealed class FailingLibrary : ILibraryStore
    {
        public Task<IReadOnlyDictionary<Guid, Ownership>> FindOwnershipsAsync(Guid linkedAccountId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Ownership>>(new Dictionary<Guid, Ownership>());

        public Task RecordAsync(
            Guid linkedAccountId,
            Guid snapshotId,
            IReadOnlyCollection<Observation> said,
            IReadOnlyCollection<Ownership> owned,
            CancellationToken ct) =>
            throw new InvalidOperationException("the library cannot be written");

        public Task<IReadOnlyList<OwnedEntry>> ListAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OwnedEntry>>([]);

        public Task<IReadOnlyList<AccountOwnership>> FindGameOwnershipsAsync(Guid ownerId, Guid gameId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AccountOwnership>>([]);

        public Task<string?> FindCoverChoiceAsync(Guid ownerId, Guid gameId, CancellationToken ct) =>
            Task.FromResult<string?>(null);

        public Task SetCoverChoiceAsync(Guid ownerId, Guid gameId, string? source, DateTimeOffset at, CancellationToken ct) =>
            throw new InvalidOperationException("the library cannot be written");

        public Task SaveAsync(IReadOnlyCollection<AccountOwnership> owned, CancellationToken ct) =>
            throw new InvalidOperationException("the library cannot be written");

        public Task<IReadOnlyList<RemovedEntry>> ListRemovedAsync(Guid ownerId, Guid? gameId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RemovedEntry>>([]);

        public Task<DateTimeOffset?> FindFirstSeenAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult<DateTimeOffset?>(null);

        public Task<IReadOnlyList<PlayedDay>> ListPlayedAsync(Guid ownerId, Guid? gameId, DateOnly? from, DateOnly? to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PlayedDay>>([]);
    }
}
