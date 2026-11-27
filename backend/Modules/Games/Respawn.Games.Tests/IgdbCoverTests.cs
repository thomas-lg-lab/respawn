using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Respawn.Games.Api;
using Respawn.Games.Ports;
using Respawn.Games.Tests.Support;
using Respawn.Testing;

namespace Respawn.Games.Tests;

/// <summary>IGDB's covers, behind Steam's: a game shows its store's cover, and IGDB's only when the store
/// has none; kept under <c>images/cover/igdb/</c> on an instance that keeps images. And what else is shown
/// of IGDB: the game's page on it, and the credit its terms ask for. Steam and IGDB answer from memory.</summary>
public sealed class IgdbCoverTests : IDisposable
{
    private const string SteamId = "76561190000000001";

    private const string KeptAWeek = "max-age=604800, private";

    /// <summary>Where IGDB's image host has Portal's cover, as the recorded games give its id.</summary>
    private const string PortalCover = "t_cover_big_2x/co0fake71.jpg";

    private static readonly byte[] Jpeg = FakeSteam.RecordedBytes("cover.jpg");

    /// <summary>Another image: IGDB's.</summary>
    private static readonly byte[] IgdbJpeg = [.. Jpeg, 7];

    private readonly string _data = Directory.CreateTempSubdirectory("respawn-data-").FullName;

    public void Dispose() => Directory.Delete(_data, recursive: true);

    [Fact]
    public async Task A_game_shows_its_stores_cover_and_igdbs_only_when_the_store_has_none()
    {
        var (steam, igdb, clock) = (new FakeSteam(), new FakeIgdb(), new TestClock());
        steam.Images["620/library_600x900.jpg"] = new(Jpeg);
        igdb.Images["t_cover_big_2x/co0fake72.jpg"] = new(IgdbJpeg);
        igdb.Images[PortalCover] = new(IgdbJpeg);
        await using var app = await StartAsync(steam, igdb, clock);
        using var browser = app.Browser();
        var games = await ReadAllAsync(app, browser);

        // Steam has Portal 2's: IGDB's is never asked for.
        using (var cover = await browser.GetAsync($"/api/v1/games/{games["Portal 2"]}/cover"))
        {
            Assert.Equal(Jpeg, await cover.Content.ReadAsByteArrayAsync());
            Assert.Equal($"\"steam-620-{Hash(Jpeg)}\"", cover.Headers.ETag?.Tag);
        }

        Assert.Empty(igdb.ImageRequests);

        // Steam's store names none for Portal, and nothing is at its usual addresses: IGDB's, kept a week
        // by the browser and as a file of its own, asked with neither the token nor the client id.
        using (var cover = await browser.GetAsync($"/api/v1/games/{games["Portal"]}/cover"))
        {
            Assert.Equal(HttpStatusCode.OK, cover.StatusCode);
            Assert.Equal(IgdbJpeg, await cover.Content.ReadAsByteArrayAsync());
            Assert.Equal("image/jpeg", cover.Content.Headers.ContentType?.ToString());
            Assert.Equal(KeptAWeek, cover.Headers.CacheControl?.ToString());
            Assert.Equal($"\"igdb-co0fake71-{Hash(IgdbJpeg)}\"", cover.Headers.ETag?.Tag);
        }

        Assert.Equal([(PortalCover, (string?)null, (string?)null)], igdb.ImageRequests);
        Assert.Contains($"cover/igdb/co0fake71-{Hash(IgdbJpeg)}.jpg", Files());

        // From the file afterwards.
        using (var again = await browser.GetAsync($"/api/v1/games/{games["Portal"]}/cover"))
        {
            Assert.Equal(IgdbJpeg, await again.Content.ReadAsByteArrayAsync());
        }

        Assert.Single(igdb.ImageRequests);
    }

    [Fact]
    public async Task Until_igdb_was_asked_about_a_game_without_a_store_cover_it_may_still_have_one()
    {
        var (steam, igdb, clock) = (new FakeSteam(), new FakeIgdb(), new TestClock());
        igdb.Images[PortalCover] = new(IgdbJpeg);
        await using var app = await StartAsync(steam, igdb, clock);
        using var browser = app.Browser();
        await LinkAsync(app, browser);
        await SyncAsync(app);
        await CoversAsync(app);
        var portal = (await GamesAsync(browser))["Portal"];

        // No cover yet, and a browser does not keep that it has none.
        using (var none = await browser.GetAsync($"/api/v1/games/{portal}/cover"))
        {
            Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
            Assert.Equal("no-store", none.Headers.CacheControl?.ToString());
        }

        await GameDataAsync(app);
        using var cover = await browser.GetAsync($"/api/v1/games/{portal}/cover");
        Assert.Equal(IgdbJpeg, await cover.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task A_cover_the_store_gets_later_takes_the_place_of_igdbs()
    {
        var (steam, igdb, clock) = (new FakeSteam(), new FakeIgdb(), new TestClock());
        igdb.Images[PortalCover] = new(IgdbJpeg);
        await using var app = await StartAsync(steam, igdb, clock);
        using var browser = app.Browser();
        var portal = (await ReadAllAsync(app, browser))["Portal"];
        using (var first = await browser.GetAsync($"/api/v1/games/{portal}/cover"))
        {
            Assert.Equal(IgdbJpeg, await first.Content.ReadAsByteArrayAsync());
        }

        // A month on, the store names a cover for Portal: it is the game's, and IGDB's file goes.
        steam.StoreBody = steam.StoreBody.Replace(
            "\"header\":\"header.jpg\",\"community_icon\":\"cfa928ab4119dd137e50d728e8fe703e4e970aff\"",
            "\"header\":\"header.jpg\",\"library_capsule\":\"library_600x900.jpg\",\"community_icon\":\"cfa928ab4119dd137e50d728e8fe703e4e970aff\"",
            StringComparison.Ordinal);
        steam.Images["400/library_600x900.jpg"] = new(Jpeg);
        clock.Advance(TimeSpan.FromDays(31));
        await CoversAsync(app);

        using var cover = await browser.GetAsync($"/api/v1/games/{portal}/cover");
        Assert.Equal(Jpeg, await cover.Content.ReadAsByteArrayAsync());
        Assert.Equal($"\"steam-400-{Hash(Jpeg)}\"", cover.Headers.ETag?.Tag);
        Assert.DoesNotContain(Files(), file => file.StartsWith("cover/igdb/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Igdbs_image_host_that_does_not_answer_is_said_and_one_without_the_image_is_no_cover()
    {
        var (steam, igdb, clock) = (new FakeSteam(), new FakeIgdb(), new TestClock());
        igdb.Images[PortalCover] = new(IgdbJpeg, Status: HttpStatusCode.ServiceUnavailable);
        await using var app = await StartAsync(steam, igdb, clock);
        using var browser = app.Browser();
        var portal = (await ReadAllAsync(app, browser))["Portal"];

        Assert.Equal((HttpStatusCode.ServiceUnavailable, "cover_unavailable"), await browser.GetAsync($"/api/v1/games/{portal}/cover").RefusalAsync());
        Assert.Empty(Files());

        // Nothing there: the game has no cover, and a browser keeps that an hour.
        igdb.Images.Remove(PortalCover);
        using var none = await browser.GetAsync($"/api/v1/games/{portal}/cover");
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal("max-age=3600, private", none.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_game_igdb_knows_leads_to_its_page_and_the_instance_credits_igdb()
    {
        var (steam, igdb, clock) = (new FakeSteam(), new FakeIgdb(), new TestClock());
        await using var app = await StartAsync(steam, igdb, clock);
        using var browser = app.Browser();
        var games = await ReadAllAsync(app, browser);

        var portal2 = await browser.GetAsync($"/api/v1/games/{games["Portal 2"]}").JsonAsync();
        Assert.Equal("igdb", portal2.GetProperty("game_data").GetProperty("source").GetString());
        Assert.Equal("https://www.igdb.com/games/portal-2", portal2.GetProperty("game_data").GetProperty("url").GetString());

        // A game IGDB does not know has no page there.
        var unknown = await browser.GetAsync($"/api/v1/games/{games[""]}").JsonAsync();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, unknown.GetProperty("game_data").ValueKind);

        var credit = Assert.Single((await browser.GetAsync("/api/v1/credits").JsonAsync()).GetProperty("credits").EnumerateArray());
        Assert.Equal(
            ("igdb", "IGDB", "https://www.igdb.com/"),
            (credit.GetProperty("source").GetString(), credit.GetProperty("label").GetString(), credit.GetProperty("url").GetString()));
    }

    [Fact]
    public async Task An_instance_that_never_used_igdb_credits_nothing_and_asks_it_for_no_cover()
    {
        var (steam, igdb) = (new FakeSteam(), new FakeIgdb());
        await using var app = await steam.StartAppAsync(services: igdb.Reach);
        using var browser = app.Browser();
        await LinkAsync(app, browser);
        await SyncAsync(app);
        await CoversAsync(app);

        Assert.Empty((await browser.GetAsync("/api/v1/credits").JsonAsync()).GetProperty("credits").EnumerateArray());
        Assert.Equal(HttpStatusCode.Unauthorized, await app.Browser().GetAsync("/api/v1/credits").StatusAsync());

        // No store cover and no IGDB: settled, as before IGDB.
        using var none = await browser.GetAsync($"/api/v1/games/{(await GamesAsync(browser))["Portal"]}/cover");
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal("max-age=3600, private", none.Headers.CacheControl?.ToString());
        Assert.Empty(igdb.ImageRequests);
    }

    /// <summary>The start of an image's SHA-256, as a kept file's name holds it.</summary>
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];

    /// <summary>The kept files, by their path under <c>images/</c>.</summary>
    private string[] Files()
    {
        var root = Path.Combine(_data, "images");
        return Directory.Exists(root)
            ? [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
                .Where(file => !file.StartsWith("tmp/", StringComparison.Ordinal))]
            : [];
    }

    /// <summary>The app, keeping its images in this test's directory and given IGDB's access; a sign-in
    /// outlives the month a test moves its clock by.</summary>
    private Task<TestApp> StartAsync(FakeSteam steam, FakeIgdb igdb, TestClock clock)
    {
        var settings = FakeIgdb.Settings();
        settings["RESPAWN_DATA_DIR"] = _data;
        settings["RESPAWN_SESSION_DAYS"] = "365";
        return steam.StartAppAsync(settings, services =>
        {
            igdb.Reach(services);
            services.AddSingleton<TimeProvider>(clock);
        });
    }

    /// <summary>A Steam account linked, its library synced, its store and IGDB asked about its games.
    /// Returns the games by name.</summary>
    private static async Task<Dictionary<string, Guid>> ReadAllAsync(TestApp app, HttpClient browser)
    {
        await LinkAsync(app, browser);
        await SyncAsync(app);
        await CoversAsync(app);
        await GameDataAsync(app);
        return await GamesAsync(browser);
    }

    private static async Task LinkAsync(TestApp app, HttpClient browser)
    {
        var owner = (await browser.RegisterAsync("first@example.org").JsonAsync()).GetProperty("id").GetGuid();
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ILibraryService>().LinkAccountAsync(owner, "steam", SteamId, null, default);
    }

    private static Task<int> SyncAsync(TestApp app) =>
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<SyncScheduler>()).RunDueAsync(default);

    private static Task<int> CoversAsync(TestApp app) =>
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<CoverScheduler>()).RunDueAsync(default);

    private static Task<int> GameDataAsync(TestApp app) =>
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<GameDataScheduler>()).RunDueAsync(default);

    private static async Task<Dictionary<string, Guid>> GamesAsync(HttpClient browser) =>
        (await browser.GetAsync("/api/v1/library?limit=500").JsonAsync()).GetProperty("games").EnumerateArray()
            .ToDictionary(game => game.GetProperty("name").GetString() ?? "", game => game.GetProperty("game_id").GetGuid());
}
