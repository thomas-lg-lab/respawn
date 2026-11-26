using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Respawn.Games.Api;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Games.Ports;
using Respawn.Games.Tests.Support;
using Respawn.Testing;

namespace Respawn.Games.Tests;

/// <summary>The covers: where Steam's store says they are, asked on a schedule of its own once a sync
/// listed the games and kept as a short text, and <c>GET /api/v1/games/{id}/cover</c>, which fetches the
/// image each time and keeps none. Steam is never called: its store and its image host answer from
/// memory, and a turn of each schedule is run by hand.</summary>
public sealed class CoverTests
{
    private const string SteamId = "76561190000000001";

    /// <summary>Where the recorded store answer says the cover of Half-Life 2 is.</summary>
    private const string Hashed = "0123456789abcdef0123456789abcdef01234567/library_capsule.jpg";

    /// <summary>As the test's browser writes the header back: the same directives, in its own order.</summary>
    private const string KeptAWeek = "max-age=604800, private";

    private static readonly byte[] Jpeg = FakeSteam.RecordedBytes("cover.jpg");

    private static readonly byte[] Png = FakeSteam.RecordedBytes("cover.png");

    [Fact]
    public async Task A_game_of_each_kind_has_its_cover_and_one_without_answers_404()
    {
        // Another image than the cover, where every game's cover used to be: what Steam's host still
        // answers there for a game whose artwork moved.
        byte[] blank = [.. Jpeg, 0];
        var (steam, clock) = (new FakeSteam(), new TestClock());
        steam.Images["620/library_600x900.jpg"] = new(Jpeg);
        steam.Images[$"220/{Hashed}"] = new(Jpeg);
        steam.Images["220/library_600x900.jpg"] = new(blank);
        steam.Images["400/portrait.png"] = new(Png, "image/png");
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        await LinkAsync(app, browser);
        Assert.Equal(1, await TurnAsync(app));
        var games = await GamesAsync(browser);

        // Where every game's cover used to be, which is where the store says this one is: asked once.
        using (var cover = await browser.GetAsync($"/api/v1/games/{games["Portal 2"]}/cover"))
        {
            Assert.Equal(HttpStatusCode.OK, cover.StatusCode);
            Assert.Equal("image/jpeg", cover.Content.Headers.ContentType?.ToString());
            Assert.Equal(Jpeg, await cover.Content.ReadAsByteArrayAsync());
            Assert.Equal(KeptAWeek, cover.Headers.CacheControl?.ToString());
            Assert.Equal(["nosniff"], cover.Headers.GetValues("X-Content-Type-Options"));
            Assert.Equal(["620/library_600x900.jpg"], Asked(steam));
        }

        // In a folder of its own: the store's word comes first, and the old address is not asked.
        using (var cover = await browser.GetAsync($"/api/v1/games/{games["Half-Life 2"]}/cover"))
        {
            Assert.Equal(HttpStatusCode.OK, cover.StatusCode);
            Assert.Equal(Jpeg, await cover.Content.ReadAsByteArrayAsync());
            Assert.Equal([$"220/{Hashed}"], Asked(steam));
        }

        // The store names no cover: the two old addresses, and the oldest form answers.
        using (var cover = await browser.GetAsync($"/api/v1/games/{games["Portal"]}/cover"))
        {
            Assert.Equal(HttpStatusCode.OK, cover.StatusCode);
            Assert.Equal("image/png", cover.Content.Headers.ContentType?.ToString());
            Assert.Equal(Png, await cover.Content.ReadAsByteArrayAsync());
            Assert.Equal(["400/library_600x900.jpg", "400/portrait.png"], Asked(steam));
        }

        // Unknown to the store, and nothing at either address: no cover, which a browser keeps an hour.
        using (var none = await browser.GetAsync($"/api/v1/games/{games[""]}/cover"))
        {
            Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
            Assert.Equal("no_cover", (await none.JsonAsync()).GetProperty("error").GetProperty("code").GetString());
            Assert.Equal("max-age=3600, private", none.Headers.CacheControl?.ToString());
            Assert.Equal(["3999990/library_600x900.jpg", "3999990/portrait.png"], Asked(steam));
        }
    }

    [Fact]
    public async Task The_store_is_asked_where_the_covers_are_once_a_sync_listed_the_games_and_without_a_key()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        steam.Images["620/library_600x900.jpg"] = new(Jpeg);
        await using var app = await StartAsync(steam, clock, new() { ["Logging:LogLevel:System.Net.Http.HttpClient"] = "Trace" });
        using var browser = app.Browser();
        await LinkAsync(app, browser);
        Assert.Equal(1, await TurnAsync(app));

        // One question for the four games, their assets and nothing else, with no key: neither in a
        // header nor in the address.
        var (address, key) = Assert.Single(steam.StoreRequests);
        Assert.Null(key);
        Assert.Equal("https://api.steampowered.com/IStoreBrowseService/GetItems/v1/", address.GetLeftPart(UriPartial.Path));
        Assert.StartsWith("?input_json=", address.Query, StringComparison.Ordinal);
        var asked = JsonDocument.Parse(Uri.UnescapeDataString(address.Query["?input_json=".Length..])).RootElement;
        Assert.Equal([220u, 400u, 620u, 3999990u], asked.GetProperty("ids").EnumerateArray().Select(id => id.GetProperty("appid").GetUInt32()).Order());
        Assert.Equal("""{"include_assets":true}""", asked.GetProperty("data_request").GetRawText());

        // What it said, kept as a short text beside each store entry: a place, or none.
        Assert.Equal(
            [("220", Hashed, clock.Now), ("3999990", null, clock.Now), ("400", null, clock.Now), ("620", "library_600x900.jpg", clock.Now)],
            await PathsAsync(app));

        // An image is asked for without a key too, and the key is nowhere in what was logged.
        var games = await GamesAsync(browser);
        Assert.Equal(HttpStatusCode.OK, await browser.GetAsync($"/api/v1/games/{games["Portal 2"]}/cover").StatusAsync());
        Assert.Equal([("620/library_600x900.jpg", null)], steam.ImageRequests);
        Assert.Contains("GetItems", app.Logs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeSteam.Key, app.Logs.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_store_is_asked_about_new_games_at_once_and_about_the_others_a_month_later()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        await LinkAsync(app, browser);
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(4, AskedAbout(steam.StoreRequests[0].Address).Length);

        // The next day, one more game: the store is asked about that one alone.
        steam.Body = FakeSteam.Recorded("owned-games.json").Replace(
            "\"game_count\":4,\"games\":[", "\"game_count\":5,\"games\":[{\"appid\":70,\"name\":\"Half-Life\",\"playtime_forever\":10},", StringComparison.Ordinal);
        steam.StoreBody = """{"response":{"store_items":[{"id":70,"success":1,"assets":{"library_capsule":"library_600x900.jpg"}}]}}""";
        clock.NextDay();
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(2, steam.StoreRequests.Count);
        Assert.Equal([70u], AskedAbout(steam.StoreRequests[1].Address));
        steam.StoreBody = FakeSteam.Recorded("store-items.json");

        // A day with nothing new asks the store nothing.
        clock.NextDay();
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(2, steam.StoreRequests.Count);

        // More than a month after the first answer, the four games of that day are asked about again:
        // artwork moves. The fifth was asked about a day later, and waits a day more.
        clock.Advance(TimeSpan.FromDays(29));
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(3, steam.StoreRequests.Count);
        Assert.Equal([220u, 400u, 620u, 3999990u], AskedAbout(steam.StoreRequests[2].Address));
    }

    [Fact]
    public async Task A_store_that_does_not_say_where_the_covers_are_costs_neither_the_sync_nor_the_covers()
    {
        var (steam, clock) = (new FakeSteam { StoreStatus = HttpStatusCode.ServiceUnavailable }, new TestClock());
        steam.Images["220/library_600x900.jpg"] = new(Jpeg);
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        await LinkAsync(app, browser);

        // The library is read and kept, and the run says so.
        Assert.Equal(1, await TurnAsync(app));
        var games = await GamesAsync(browser);
        Assert.Equal(4, games.Count);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var run = await scope.ServiceProvider.GetRequiredService<GamesDbContext>().SyncRuns.AsNoTracking().SingleAsync();
            Assert.Equal(("succeeded", null), (run.Outcome, run.Error));
        }

        // Nothing is known of the covers' places, and nothing says the store was asked. The log
        // says the trouble, once; the store is left alone for an hour, then asked again without a
        // word more while the trouble is the same.
        Assert.All(await PathsAsync(app), entry => Assert.Equal((null, null), (entry.Path, entry.AskedAt)));
        Assert.Equal(1, app.Logs.Count("covers: linked account"));
        Assert.Equal(0, await CoversAsync(app));
        Assert.Single(steam.StoreRequests);
        clock.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(1, await CoversAsync(app));
        Assert.Equal(2, steam.StoreRequests.Count);
        Assert.Equal(1, app.Logs.Count("covers: linked account"));

        // A cover is still found where every game's cover used to be. It may not be the right
        // image: a browser keeps it an hour, not a week, and does not keep that a game has none.
        using (var cover = await browser.GetAsync($"/api/v1/games/{games["Half-Life 2"]}/cover"))
        {
            Assert.Equal(HttpStatusCode.OK, cover.StatusCode);
            Assert.Equal("max-age=3600, private", cover.Headers.CacheControl?.ToString());
            Assert.Equal(["220/library_600x900.jpg"], Asked(steam));
        }

        using (var none = await browser.GetAsync($"/api/v1/games/{games["Portal"]}/cover"))
        {
            Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
            Assert.Equal("no-store", none.Headers.CacheControl?.ToString());
            Asked(steam);
        }

        // The next sync asks again about every game, and keeps what the store says.
        steam.StoreStatus = HttpStatusCode.OK;
        clock.NextDay();
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(4, AskedAbout(steam.StoreRequests[^1].Address).Length);
        Assert.Contains(("220", Hashed, clock.Now), await PathsAsync(app));
        Assert.Contains("covers: linked account", app.Logs.Text[app.Logs.Text.LastIndexOf("is read again", StringComparison.Ordinal)..], StringComparison.Ordinal);

        // From then on the answers are kept as long as any other.
        steam.Images[$"220/{Hashed}"] = new(Jpeg);
        using (var cover = await browser.GetAsync($"/api/v1/games/{games["Half-Life 2"]}/cover"))
        {
            Assert.Equal(KeptAWeek, cover.Headers.CacheControl?.ToString());
        }
    }

    [Fact]
    public async Task A_large_library_is_asked_about_fifty_games_at_a_time()
    {
        var (steam, clock) = (new FakeSteam { Body = Library(120), StoreBody = Store(120) }, new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        await LinkAsync(app, browser);

        Assert.Equal(1, await TurnAsync(app));

        Assert.Equal([50, 50, 20], steam.StoreRequests.Select(request => AskedAbout(request.Address).Length));
        Assert.Equal(120, steam.StoreRequests.SelectMany(request => AskedAbout(request.Address)).Distinct().Count());
        Assert.All(await PathsAsync(app), entry => Assert.Equal(("library_600x900.jpg", clock.Now), (entry.Path, entry.AskedAt)));
    }

    [Fact]
    public async Task A_store_that_stops_answering_half_way_keeps_what_it_said_and_is_asked_the_rest_later()
    {
        var (steam, clock) = (new FakeSteam { Body = Library(120), StoreBody = Store(120) }, new TestClock());
        steam.StoreAnswerFor = (rank, _) => rank == 1 ? (HttpStatusCode.ServiceUnavailable, "") : null;
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        await LinkAsync(app, browser);

        // The second question fails: the first one's fifty games are kept, the third is not asked.
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(2, steam.StoreRequests.Count);
        Assert.Equal(50, (await PathsAsync(app)).Count(entry => entry.AskedAt is not null));

        // The next sync asks about the seventy left, and no other.
        steam.StoreAnswerFor = null;
        clock.NextDay();
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal([50, 50, 50, 20], steam.StoreRequests.Select(request => AskedAbout(request.Address).Length));
        Assert.All(await PathsAsync(app), entry => Assert.NotNull(entry.AskedAt));
    }

    [Fact]
    public async Task An_answer_that_names_none_of_the_games_teaches_nothing_about_them()
    {
        // The store answers, and says nothing: a store that could not be read, not four games
        // without a cover for a month.
        var (steam, clock) = (new FakeSteam { StoreBody = """{"response":{}}""" }, new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        await LinkAsync(app, browser);

        Assert.Equal(1, await TurnAsync(app));
        Assert.All(await PathsAsync(app), entry => Assert.Equal((null, null), (entry.Path, entry.AskedAt)));
        Assert.Contains("names none of the games", app.Logs.Text, StringComparison.Ordinal);

        // The next day all of them are asked about again; a game the store names without a cover is
        // kept as such, and so is the one it does not know.
        steam.StoreBody = FakeSteam.Recorded("store-items.json");
        clock.NextDay();
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(4, AskedAbout(steam.StoreRequests[^1].Address).Length);
        Assert.All(await PathsAsync(app), entry => Assert.Equal(clock.Now, entry.AskedAt));
    }

    [Fact]
    public async Task A_sync_asked_for_by_its_owner_does_not_wait_for_the_store_which_is_asked_at_the_next_turn()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        var answer = await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("succeeded", (await answer.JsonAsync()).GetProperty("outcome").GetString());
        Assert.Empty(steam.StoreRequests);

        Assert.Equal(1, await CoversAsync(app));
        Assert.Single(steam.StoreRequests);
        Assert.Contains(("220", Hashed, clock.Now), await PathsAsync(app));

        // Nothing is left to ask: the schedule has no account due.
        Assert.Equal(0, await CoversAsync(app));
    }

    [Fact]
    public async Task A_fault_while_the_store_is_asked_is_said_and_costs_the_sync_nothing()
    {
        // Not a store that fails: the connector itself, as a fault of the app would.
        var (steam, clock) = (new FakeSteam(), new TestClock());
        await using var app = await steam.StartAppAsync(services: services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.RemoveAll<ICoverSource>();
            services.AddSingleton<ICoverSource>(new BrokenCovers());
        });
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        Assert.Equal(1, await TurnAsync(app));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.OK, await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null).StatusAsync());
        await CoversAsync(app);

        await using var scope = app.Services.CreateAsyncScope();
        var runs = await scope.ServiceProvider.GetRequiredService<GamesDbContext>().SyncRuns.AsNoTracking().ToListAsync();
        Assert.Equal(["succeeded", "succeeded"], runs.Select(run => run.Outcome));

        // In the log, once: the account is left alone for an hour after a try.
        Assert.Equal(1, app.Logs.Count("covers: the turn of linked account"));
    }

    [Fact]
    public async Task A_client_that_asks_for_too_many_covers_is_refused()
    {
        var steam = new FakeSteam();
        await using var app = await steam.StartAppAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();
        var none = $"/api/v1/games/{Guid.CreateVersion7()}/cover";

        // As many as the largest screen of covers asks for, in a row; the bucket fills again each
        // minute while they are asked, so the refusal comes at the two thousandth or somewhat after.
        // The refusal checked is the one that ended the run: a request after it could land after the
        // bucket filled again, on a busy machine.
        var answered = 0;
        (HttpStatusCode Status, string? Code) refusal = default;
        while (answered < 4000)
        {
            using var answer = await browser.GetAsync(none);
            if (answer.StatusCode != HttpStatusCode.NotFound)
            {
                refusal = (answer.StatusCode, (await answer.JsonAsync()).GetProperty("error").GetProperty("code").GetString());
                break;
            }

            answered++;
        }

        Assert.InRange(answered, 2000, 3999);
        Assert.Equal((HttpStatusCode.TooManyRequests, "too_many_requests"), refusal);
    }

    [Fact]
    public async Task Only_a_path_of_the_known_shape_is_kept_or_ever_asked_of_the_image_host()
    {
        // What a store gone wrong could say: a way up, another host, a query, a deeper folder, a file
        // that is no image, and a game nobody asked about.
        static string Item(int id, string capsule) =>
            JsonSerializer.Serialize(new { id, success = 1, appid = id, assets = new { library_capsule = capsule } });
        var library = JsonSerializer.Serialize(new
        {
            response = new
            {
                game_count = 6,
                games = new[] { 10, 20, 30, 40, 50, 60 }.Select(appId => new { appid = appId, name = $"Game {appId}", playtime_forever = 1 }),
            },
        });
        var store = "{\"response\":{\"store_items\":["
            + string.Join(',', Item(10, "../../../somewhere/else.jpg"), Item(20, "https://example.org/cover.jpg"), Item(30, "library_600x900.jpg?t=1"),
                Item(40, "a/b/cover.jpg"), Item(50, "cover.svg"), Item(60, "folder-1/cover_2x.png"), Item(70, "library_600x900.jpg"))
            + "]}}";
        var (steam, clock) = (new FakeSteam { Body = library, StoreBody = store }, new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        await LinkAsync(app, browser);
        Assert.Equal(1, await TurnAsync(app));

        Assert.Equal(
            [("10", null), ("20", null), ("30", null), ("40", null), ("50", null), ("60", "folder-1/cover_2x.png")],
            (await PathsAsync(app)).Select(entry => (entry.ExternalId, entry.Path)));

        // And a path that would be in the database all the same is not sent anywhere either.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            await db.StoreEntries.Where(e => e.ExternalId == "10").ExecuteUpdateAsync(e => e.SetProperty(x => x.CoverPath, "../../../somewhere/else.jpg"));
        }

        var games = await GamesAsync(browser);
        Assert.Equal((HttpStatusCode.NotFound, "no_cover"), await browser.GetAsync($"/api/v1/games/{games["Game 10"]}/cover").RefusalAsync());
        Assert.Equal(["10/library_600x900.jpg", "10/portrait.png"], Asked(steam));
    }

    [Fact]
    public async Task What_the_image_host_answers_is_passed_on_only_when_it_is_an_image_of_an_accepted_size()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        await LinkAsync(app, browser);
        Assert.Equal(1, await TurnAsync(app));
        var portal2 = $"/api/v1/games/{(await GamesAsync(browser))["Portal 2"]}/cover";

        // A page that calls itself an image, then an image too large: neither is a cover.
        byte[] tooLarge = [.. Png, .. new byte[5 * 1024 * 1024]];
        steam.Images["620/library_600x900.jpg"] = new("<html>not an image</html>"u8.ToArray());
        steam.Images["620/portrait.png"] = new(tooLarge, "image/png");
        Assert.Equal((HttpStatusCode.NotFound, "no_cover"), await browser.GetAsync(portal2).RefusalAsync());
        Assert.Equal(["620/library_600x900.jpg", "620/portrait.png"], Asked(steam));

        // An answer that sends elsewhere is not followed: nothing is asked of where it sends, and it
        // is the host's trouble, not a game without a cover.
        steam.Images["620/library_600x900.jpg"] = new([], "text/html", HttpStatusCode.Found, "https://example.org/cover.jpg");
        steam.Images.Remove("620/portrait.png");
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "cover_unavailable"), await browser.GetAsync(portal2).RefusalAsync());
        Assert.Equal(["620/library_600x900.jpg"], Asked(steam));

        // The kind of image is read from its bytes, whatever the host calls it.
        steam.Images["620/library_600x900.jpg"] = new(Png, "text/html");
        using (var cover = await browser.GetAsync(portal2))
        {
            Assert.Equal(HttpStatusCode.OK, cover.StatusCode);
            Assert.Equal("image/png", cover.Content.Headers.ContentType?.ToString());
        }

        steam.Images["620/library_600x900.jpg"] = new([.. "RIFF"u8, 4, 0, 0, 0, .. "WEBP"u8], "application/octet-stream");
        using (var cover = await browser.GetAsync(portal2))
        {
            Assert.Equal("image/webp", cover.Content.Headers.ContentType?.ToString());
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task An_image_host_that_refuses_or_does_not_answer_is_said_and_not_kept(HttpStatusCode status)
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        await LinkAsync(app, browser);
        Assert.Equal(1, await TurnAsync(app));
        var games = await GamesAsync(browser);
        steam.ImagesStatus = status;

        using var cover = await browser.GetAsync($"/api/v1/games/{games["Portal 2"]}/cover");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, cover.StatusCode);
        Assert.Equal("cover_unavailable", (await cover.JsonAsync()).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("no-store", cover.Headers.CacheControl?.ToString());
        // Not asked again at the next address: the host is the same.
        Assert.Single(steam.ImageRequests);
    }

    [Fact]
    public async Task A_cover_is_for_any_signed_in_account_and_for_a_game_of_the_catalogue()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        steam.Images["620/library_600x900.jpg"] = new(Jpeg);
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        await LinkAsync(app, browser);
        Assert.Equal(1, await TurnAsync(app));
        var portal2 = (await GamesAsync(browser))["Portal 2"];

        // Nobody signed in.
        using var visitor = app.Browser();
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await visitor.GetAsync($"/api/v1/games/{portal2}/cover").RefusalAsync());
        Assert.Empty(steam.ImageRequests);

        // An account that owns nothing: a cover is the catalogue's, not a library's.
        TestDatabase.Run("INSERT INTO accounts.settings (key, value) VALUES ('registration_open', 'true') ON CONFLICT DO NOTHING");
        using var other = app.Browser();
        await other.RegisterAsync("second@example.org");
        Assert.Equal(HttpStatusCode.OK, await other.GetAsync($"/api/v1/games/{portal2}/cover").StatusAsync());

        // No such game, and what is not an id.
        Assert.Equal((HttpStatusCode.NotFound, "unknown_game"), await browser.GetAsync($"/api/v1/games/{Guid.CreateVersion7()}/cover").RefusalAsync());
        Assert.Equal(HttpStatusCode.NotFound, await browser.GetAsync("/api/v1/games/portal-2/cover").StatusAsync());
        Assert.Single(steam.ImageRequests);
    }

    [Fact]
    public async Task What_is_no_app_id_is_never_sent_to_the_store_and_is_answered_as_a_game_it_does_not_know()
    {
        var steam = new FakeSteam();
        await using var app = await steam.StartAppAsync();
        var source = app.Services.GetRequiredService<ICoverSource>();

        var paths = await source.ReadCoverPathsAsync(["620", "0620", "62 0", "4294967296", "portal"], default);

        Assert.Equal([620u], AskedAbout(Assert.Single(steam.StoreRequests).Address));
        Assert.Equal(
            [("0620", null), ("4294967296", null), ("62 0", null), ("620", "library_600x900.jpg"), ("portal", null)],
            paths.OrderBy(path => path.Key, StringComparer.Ordinal).Select(path => (path.Key, path.Value)));

        // And none at all asks nothing.
        Assert.Equal([("portal", null)], (await source.ReadCoverPathsAsync(["portal"], default)).Select(path => (path.Key, path.Value)));
        Assert.Single(steam.StoreRequests);
    }

    /// <summary>Steam's answer for a library of that many games, whose app ids are 10, 20, 30...</summary>
    private static string Library(int games) =>
        JsonSerializer.Serialize(new
        {
            response = new
            {
                game_count = games,
                games = Enumerable.Range(1, games).Select(rank => new { appid = rank * 10, name = $"Game {rank}", playtime_forever = rank }),
            },
        });

    /// <summary>The store's answer for the same games, each with its cover where covers used to be.</summary>
    private static string Store(int games) =>
        JsonSerializer.Serialize(new
        {
            response = new
            {
                store_items = Enumerable.Range(1, games).Select(rank => new { id = rank * 10, success = 1, assets = new { library_capsule = "library_600x900.jpg" } }),
            },
        });

    /// <summary>The files asked of the image host since the last look, in order.</summary>
    private static string[] Asked(FakeSteam steam)
    {
        string[] asked = [.. steam.ImageRequests.Select(request => request.Path)];
        steam.ImageRequests.Clear();
        return asked;
    }

    /// <summary>The app ids one question to the store is about, the lowest first.</summary>
    private static uint[] AskedAbout(Uri address) =>
    [
        .. JsonDocument.Parse(Uri.UnescapeDataString(address.Query["?input_json=".Length..])).RootElement
            .GetProperty("ids").EnumerateArray().Select(id => id.GetProperty("appid").GetUInt32()).Order(),
    ];

    /// <summary>What is kept of the covers' places, by the game's id in the store.</summary>
    private static async Task<(string ExternalId, string? Path, DateTimeOffset? AskedAt)[]> PathsAsync(TestApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<GamesDbContext>().StoreEntries.AsNoTracking().ToListAsync();
        return [.. entries.OrderBy(e => e.ExternalId, StringComparer.Ordinal).Select(e => (e.ExternalId, e.CoverPath, e.CoverAskedAt))];
    }

    /// <summary>The ids of the library's games by name; the game Steam gives no name for is under "".</summary>
    private static async Task<Dictionary<string, Guid>> GamesAsync(HttpClient browser) =>
        (await browser.GetAsync("/api/v1/library?limit=500").JsonAsync()).GetProperty("games").EnumerateArray()
            .ToDictionary(game => game.GetProperty("name").GetString() ?? "", game => game.GetProperty("game_id").GetGuid());

    private static Task<TestApp> StartAsync(FakeSteam steam, TestClock clock, Dictionary<string, string?>? settings = null) =>
        steam.StartAppAsync(settings, services => services.AddSingleton<TimeProvider>(clock));

    /// <summary>An account of the instance, signed in, with a Steam account linked to it.</summary>
    private static async Task<Guid> LinkAsync(TestApp app, HttpClient browser)
    {
        var owner = (await browser.RegisterAsync("first@example.org").JsonAsync()).GetProperty("id").GetGuid();
        await using var scope = app.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<ILibraryService>().LinkAccountAsync(owner, "steam", SteamId, null, default)).Id;
    }

    /// <summary>One turn of the sync's schedule then one of the covers', as the app's timers run them.
    /// Returns how many syncs ended.</summary>
    private static async Task<int> TurnAsync(TestApp app)
    {
        var synced = await Assert.Single(app.Services.GetServices<IHostedService>().OfType<SyncScheduler>()).RunDueAsync(default);
        await CoversAsync(app);
        return synced;
    }

    /// <summary>One turn of the covers' schedule. Returns how many linked accounts' stores were asked.</summary>
    private static Task<int> CoversAsync(TestApp app) =>
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<CoverScheduler>()).RunDueAsync(default);

    /// <summary>A source of covers that fails as no store does.</summary>
    private sealed class BrokenCovers : ICoverSource
    {
        public string Source => "steam";

        public int PathsPerRequest => 50;

        public Task<Cover?> ReadCoverAsync(string externalId, string? path, CancellationToken ct) =>
            throw new InvalidOperationException("broken");

        public Task<IReadOnlyDictionary<string, string?>> ReadCoverPathsAsync(IReadOnlyCollection<string> externalIds, CancellationToken ct) =>
            throw new InvalidOperationException("broken");
    }
}
