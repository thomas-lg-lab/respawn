using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Respawn.Games.Api;
using Respawn.Games.Ports;
using Respawn.Games.Tests.Support;
using Respawn.Testing;

namespace Respawn.Games.Tests;

/// <summary>The cover an account chooses for a game, among the sources that have one: its own, shown in
/// the game's page and its library, asked for by source, every other account keeping the game's own.
/// Steam and IGDB answer from memory.</summary>
public sealed class CoverChoiceTests : IDisposable
{
    private const string SteamId = "76561190000000001";

    /// <summary>Where IGDB's image host has Portal 2's cover, as the recorded games give its id.</summary>
    private const string Portal2Igdb = "t_cover_big_2x/co0fake72.jpg";

    private static readonly byte[] Jpeg = FakeSteam.RecordedBytes("cover.jpg");

    /// <summary>Another image: IGDB's.</summary>
    private static readonly byte[] IgdbJpeg = [.. Jpeg, 7];

    private readonly string _data = Directory.CreateTempSubdirectory("respawn-data-").FullName;

    public void Dispose() => Directory.Delete(_data, recursive: true);

    [Fact]
    public async Task An_account_chooses_igdbs_cover_for_itself_alone_and_goes_back_to_the_games_own()
    {
        var (steam, igdb) = (new FakeSteam(), new FakeIgdb());
        steam.Images["620/library_600x900.jpg"] = new(Jpeg);
        igdb.Images[Portal2Igdb] = new(IgdbJpeg);
        await using var app = await StartAsync(steam, igdb, keeps: true);
        using var browser = app.Browser();
        var portal2 = (await ReadAllAsync(app, browser))["Portal 2"];

        // Steam's store says where its cover is, and IGDB has one: two to choose from, Steam's first.
        Assert.Equal(("steam,igdb", null), Covers(await GameAsync(browser, portal2)));

        var chosen = await browser.PutAsJsonAsync($"/api/v1/games/{portal2}/cover-choice", new { source = "igdb" }).JsonAsync();
        Assert.Equal(("steam,igdb", "igdb"), Covers(chosen));
        Assert.Equal(("steam,igdb", "igdb"), Covers(await GameAsync(browser, portal2)));
        Assert.Equal("igdb", (await LibraryGameAsync(browser, portal2)).GetProperty("cover_source").GetString());

        // Asked by its source, IGDB's cover, kept apart from the game's own; the game's own is Steam's still.
        using (var cover = await browser.GetAsync($"/api/v1/games/{portal2}/cover?source=igdb"))
        {
            Assert.Equal(IgdbJpeg, await cover.Content.ReadAsByteArrayAsync());
            Assert.Equal($"\"igdb-co0fake72-{Hash(IgdbJpeg)}\"", cover.Headers.ETag?.Tag);
        }

        using (var own = await browser.GetAsync($"/api/v1/games/{portal2}/cover"))
        {
            Assert.Equal(Jpeg, await own.Content.ReadAsByteArrayAsync());
        }

        Assert.Contains($"cover/igdb/co0fake72-{Hash(IgdbJpeg)}.jpg", Files());
        Assert.Contains($"cover/steam/620-{Hash(Jpeg)}.jpg", Files());
        using (var again = await browser.GetAsync($"/api/v1/games/{portal2}/cover?source=igdb"))
        {
            Assert.Equal(IgdbJpeg, await again.Content.ReadAsByteArrayAsync());
        }

        Assert.Single(igdb.ImageRequests);

        // Another account sees the game's own, and chose nothing.
        TestDatabase.Run("INSERT INTO accounts.settings (key, value) VALUES ('registration_open', 'true') ON CONFLICT DO NOTHING");
        using var other = app.Browser();
        await other.RegisterAsync("second@example.org");
        Assert.Equal(("steam,igdb", null), Covers(await GameAsync(other, portal2)));

        // Back to the game's own: by forgetting the choice, or by choosing Steam's, which is no choice.
        Assert.Equal(("steam,igdb", null), Covers(await browser.DeleteAsync($"/api/v1/games/{portal2}/cover-choice").JsonAsync()));
        Assert.Equal(JsonValueKind.Null, (await LibraryGameAsync(browser, portal2)).GetProperty("cover_source").ValueKind);
        await browser.PutAsJsonAsync($"/api/v1/games/{portal2}/cover-choice", new { source = "igdb" });
        Assert.Equal(("steam,igdb", null), Covers(await browser.PutAsJsonAsync($"/api/v1/games/{portal2}/cover-choice", new { source = "steam" }).JsonAsync()));
        Assert.Equal(("steam,igdb", null), Covers(await GameAsync(browser, portal2)));
    }

    [Fact]
    public async Task Only_a_source_the_game_has_a_cover_from_is_chosen()
    {
        var (steam, igdb) = (new FakeSteam(), new FakeIgdb());
        await using var app = await StartAsync(steam, igdb, keeps: false);
        using var browser = app.Browser();
        var games = await ReadAllAsync(app, browser);

        // Portal: Steam's store names no cover, IGDB has one; nothing to choose but IGDB's own.
        Assert.Equal(("igdb", null), Covers(await GameAsync(browser, games["Portal"])));
        Assert.Equal(
            (HttpStatusCode.Conflict, "no_such_cover"),
            await browser.PutAsJsonAsync($"/api/v1/games/{games["Portal"]}/cover-choice", new { source = "steam" }).RefusalAsync());
        Assert.Equal(
            (HttpStatusCode.Conflict, "no_such_cover"),
            await browser.PutAsJsonAsync($"/api/v1/games/{games["Portal 2"]}/cover-choice", new { source = "elsewhere" }).RefusalAsync());
        Assert.Equal(
            (HttpStatusCode.NotFound, "unknown_game"),
            await browser.PutAsJsonAsync($"/api/v1/games/{Guid.CreateVersion7()}/cover-choice", new { source = "igdb" }).RefusalAsync());
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            await browser.PutAsJsonAsync($"/api/v1/games/{games["Portal 2"]}/cover-choice", new { }).StatusAsync());
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await app.Browser().PutAsJsonAsync($"/api/v1/games/{games["Portal 2"]}/cover-choice", new { source = "igdb" }).StatusAsync());
    }

    [Fact]
    public async Task A_source_with_no_cover_for_the_game_gives_the_games_own()
    {
        var (steam, igdb) = (new FakeSteam(), new FakeIgdb());
        steam.Images["620/library_600x900.jpg"] = new(Jpeg);
        await using var app = await StartAsync(steam, igdb, keeps: true);
        using var browser = app.Browser();
        var portal2 = (await ReadAllAsync(app, browser))["Portal 2"];

        // IGDB's image host has nothing at the cover's place, and an unknown source is no source.
        foreach (var source in new[] { "igdb", "elsewhere" })
        {
            using var cover = await browser.GetAsync($"/api/v1/games/{portal2}/cover?source={source}");
            Assert.Equal(Jpeg, await cover.Content.ReadAsByteArrayAsync());
            Assert.Equal($"\"steam-620-{Hash(Jpeg)}\"", cover.Headers.ETag?.Tag);
        }
    }

    [Fact]
    public async Task An_instance_that_keeps_no_image_passes_the_chosen_sources_cover_on()
    {
        var (steam, igdb) = (new FakeSteam(), new FakeIgdb());
        steam.Images["620/library_600x900.jpg"] = new(Jpeg);
        igdb.Images[Portal2Igdb] = new(IgdbJpeg);
        await using var app = await StartAsync(steam, igdb, keeps: false);
        using var browser = app.Browser();
        var portal2 = (await ReadAllAsync(app, browser))["Portal 2"];

        using var cover = await browser.GetAsync($"/api/v1/games/{portal2}/cover?source=igdb");
        Assert.Equal(IgdbJpeg, await cover.Content.ReadAsByteArrayAsync());
        Assert.Null(cover.Headers.ETag);
        Assert.Empty(Files());
    }

    /// <summary>A game's covers, or an answer that is them: the sources in their order, and the one chosen.</summary>
    private static (string Sources, string? Chosen) Covers(JsonElement answer)
    {
        var covers = answer.TryGetProperty("covers", out var inside) ? inside : answer;
        return (
            string.Join(',', covers.GetProperty("sources").EnumerateArray().Select(source => source.GetString())),
            covers.GetProperty("chosen").GetString());
    }

    private static Task<JsonElement> GameAsync(HttpClient browser, Guid game) => browser.GetAsync($"/api/v1/games/{game}").JsonAsync();

    private static async Task<JsonElement> LibraryGameAsync(HttpClient browser, Guid game) =>
        (await browser.GetAsync("/api/v1/library?limit=500").JsonAsync()).GetProperty("games").EnumerateArray()
            .Single(item => item.GetProperty("game_id").GetGuid() == game);

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

    /// <summary>The app given IGDB's access, keeping its images in this test's directory or none.</summary>
    private Task<TestApp> StartAsync(FakeSteam steam, FakeIgdb igdb, bool keeps)
    {
        var settings = FakeIgdb.Settings();
        if (keeps)
        {
            settings["RESPAWN_DATA_DIR"] = _data;
        }

        return steam.StartAppAsync(settings, igdb.Reach);
    }

    /// <summary>A Steam account linked, its library synced, its store and IGDB asked about its games.
    /// Returns the games by name.</summary>
    private static async Task<Dictionary<string, Guid>> ReadAllAsync(TestApp app, HttpClient browser)
    {
        var owner = (await browser.RegisterAsync("first@example.org").JsonAsync()).GetProperty("id").GetGuid();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILibraryService>().LinkAccountAsync(owner, "steam", SteamId, null, default);
        }

        await Assert.Single(app.Services.GetServices<IHostedService>().OfType<SyncScheduler>()).RunDueAsync(default);
        await Assert.Single(app.Services.GetServices<IHostedService>().OfType<CoverScheduler>()).RunDueAsync(default);
        await Assert.Single(app.Services.GetServices<IHostedService>().OfType<GameDataScheduler>()).RunDueAsync(default);
        return (await browser.GetAsync("/api/v1/library?limit=500").JsonAsync()).GetProperty("games").EnumerateArray()
            .ToDictionary(game => game.GetProperty("name").GetString() ?? "", game => game.GetProperty("game_id").GetGuid());
    }
}
