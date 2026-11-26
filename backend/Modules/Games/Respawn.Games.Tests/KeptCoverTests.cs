using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Respawn.Games.Api;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Games.Ports;
using Respawn.Games.Tests.Support;
using Respawn.Testing;

namespace Respawn.Games.Tests;

/// <summary>The covers an instance keeps once it is given a data directory: fetched from Steam the first
/// time each is asked for, written under <c>images/cover/steam/</c>, then served from the file with an
/// ETag, asked of Steam again once its store said again where it is, and still served when Steam does
/// not answer or no longer has it. Steam answers from memory; each test has a directory of its own.</summary>
public sealed class KeptCoverTests : IDisposable
{
    private const string SteamId = "76561190000000001";

    private const string KeptAWeek = "max-age=604800, private";

    private const string KeptAnHour = "max-age=3600, private";

    private static readonly byte[] Jpeg = FakeSteam.RecordedBytes("cover.jpg");

    /// <summary>Another image at the same address: a cover Steam changed.</summary>
    private static readonly byte[] Changed = [.. Jpeg, 1];

    private readonly string _data = Directory.CreateTempSubdirectory("respawn-data-").FullName;

    public void Dispose() => Directory.Delete(_data, recursive: true);

    [Fact]
    public async Task A_cover_is_fetched_once_written_and_served_from_its_file_with_an_etag()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        steam.Images["620/library_600x900.jpg"] = new(Jpeg);
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var portal2 = await LinkAndListAsync(app, browser);

        var first = await CoverAsync(browser, portal2);
        Assert.Equal(Jpeg, first.Bytes);
        Assert.Equal(KeptAWeek, first.CacheControl);
        var tag = Assert.IsType<EntityTagHeaderValue>(first.Tag);
        Assert.Equal($"\"steam-620-{Hash(Jpeg)}\"", tag.Tag);
        Assert.Equal([$"cover/steam/620-{Hash(Jpeg)}.jpg"], Files());

        // From the file: Steam is not asked again, and the name is the same.
        var second = await CoverAsync(browser, portal2);
        Assert.Equal(Jpeg, second.Bytes);
        Assert.Equal("image/jpeg", second.ContentType);
        Assert.Equal(tag, second.Tag);
        Assert.Single(steam.ImageRequests);

        // A browser that holds it asks whether it changed: no body.
        using var asked = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/games/{portal2}/cover");
        asked.Headers.IfNoneMatch.Add(tag);
        using var notModified = await browser.SendAsync(asked);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Empty(await notModified.Content.ReadAsByteArrayAsync());
        Assert.Single(steam.ImageRequests);

        // The database says which image the game shows, the file what it holds.
        Assert.Equal([("cover", "steam", $"620-{Hash(Jpeg)}", "image/jpeg", clock.Now)], await ImagesAsync(app));
        Assert.Equal(1, app.Logs.Count("covers are kept in the data directory"));
    }

    [Fact]
    public async Task Without_a_data_directory_a_cover_is_fetched_at_each_request_and_has_no_etag()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        steam.Images["620/library_600x900.jpg"] = new(Jpeg);
        await using var app = await steam.StartAppAsync(null, services => services.AddSingleton<TimeProvider>(clock));
        using var browser = app.Browser();
        var portal2 = await LinkAndListAsync(app, browser);

        for (var i = 0; i < 2; i++)
        {
            var cover = await CoverAsync(browser, portal2);
            Assert.Equal(Jpeg, cover.Bytes);
            Assert.Null(cover.Tag);
        }

        Assert.Equal(2, steam.ImageRequests.Count);
        Assert.Empty(await ImagesAsync(app));
        Assert.Equal(1, app.Logs.Count("no data directory (RESPAWN_DATA_DIR): covers are not kept"));
    }

    [Fact]
    public async Task A_cover_found_before_the_store_said_where_it_is_is_passed_on_and_not_kept()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        steam.Images["620/library_600x900.jpg"] = new(Jpeg);
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        await LinkAsync(app, browser);

        // The sync listed the games; the store was not asked yet where their covers are.
        await Assert.Single(app.Services.GetServices<IHostedService>().OfType<SyncScheduler>()).RunDueAsync(default);
        var portal2 = (await GamesAsync(browser))["Portal 2"];
        var early = await CoverAsync(browser, portal2);
        Assert.Equal(Jpeg, early.Bytes);
        Assert.Equal(KeptAnHour, early.CacheControl);
        Assert.Null(early.Tag);
        Assert.Empty(Files());

        // Once it was: kept.
        await CoversAsync(app);
        Assert.NotNull((await CoverAsync(browser, portal2)).Tag);
        Assert.Single(Files());
    }

    [Fact]
    public async Task Once_its_store_was_asked_again_a_cover_is_fetched_again_and_a_changed_one_replaces_the_file()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        steam.Images["620/library_600x900.jpg"] = new(Jpeg);
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var portal2 = await LinkAndListAsync(app, browser);
        var first = await CoverAsync(browser, portal2);

        // A month later the store is asked again; the image is the same: same file, same name.
        clock.Advance(CoverSchedule.AskAgainAfter + TimeSpan.FromDays(1));
        Assert.Equal(1, await CoversAsync(app));
        var same = await CoverAsync(browser, portal2);
        Assert.Equal(first.Tag, same.Tag);
        Assert.Equal(2, steam.ImageRequests.Count);
        Assert.Equal([$"cover/steam/620-{Hash(Jpeg)}.jpg"], Files());
        Assert.Equal(clock.Now, Assert.Single(await ImagesAsync(app)).CheckedAt);

        // Another month, and Steam changed it: a new file and a new name; the old one is gone.
        steam.Images["620/library_600x900.jpg"] = new(Changed);
        clock.Advance(CoverSchedule.AskAgainAfter + TimeSpan.FromDays(1));
        Assert.Equal(1, await CoversAsync(app));
        var changed = await CoverAsync(browser, portal2);
        Assert.Equal(Changed, changed.Bytes);
        Assert.NotEqual(first.Tag, changed.Tag);
        Assert.Equal([$"cover/steam/620-{Hash(Changed)}.jpg"], Files());

        // And served from there.
        Assert.Equal(Changed, (await CoverAsync(browser, portal2)).Bytes);
        Assert.Equal(3, steam.ImageRequests.Count);
    }

    [Fact]
    public async Task A_kept_cover_is_still_served_when_steam_does_not_answer_or_no_longer_has_it()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        steam.Images["620/library_600x900.jpg"] = new(Jpeg);
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var portal2 = await LinkAndListAsync(app, browser);
        await CoverAsync(browser, portal2);
        clock.Advance(CoverSchedule.AskAgainAfter + TimeSpan.FromDays(1));
        await CoversAsync(app);

        // Steam's host is down: the kept image, for an hour, and it is asked again next time.
        steam.ImagesStatus = HttpStatusCode.ServiceUnavailable;
        var down = await CoverAsync(browser, portal2);
        Assert.Equal(Jpeg, down.Bytes);
        Assert.Equal(KeptAnHour, down.CacheControl);
        Assert.Equal(2, steam.ImageRequests.Count);

        // Its store page is gone: Steam has no image at any address. The kept one is the game's cover
        // now, settled, and Steam is not asked again before a month.
        steam.ImagesStatus = null;
        steam.Images.Clear();
        var gone = await CoverAsync(browser, portal2);
        Assert.Equal(Jpeg, gone.Bytes);
        Assert.Equal(KeptAWeek, gone.CacheControl);
        Assert.Equal(4, steam.ImageRequests.Count);
        Assert.Equal(Jpeg, (await CoverAsync(browser, portal2)).Bytes);
        Assert.Equal(4, steam.ImageRequests.Count);
        Assert.Single(Files());
    }

    [Fact]
    public async Task A_file_gone_from_the_directory_is_fetched_and_written_again()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        steam.Images["620/library_600x900.jpg"] = new(Jpeg);
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var portal2 = await LinkAndListAsync(app, browser);
        await CoverAsync(browser, portal2);

        File.Delete(Path.Combine(_data, "images", Files()[0]));
        Assert.Equal(Jpeg, (await CoverAsync(browser, portal2)).Bytes);
        Assert.Equal(2, steam.ImageRequests.Count);
        Assert.Single(Files());
    }

    [Fact]
    public async Task Two_requests_for_a_cover_at_once_cost_steam_one()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        steam.Images["620/library_600x900.jpg"] = new(Jpeg);
        await using var app = await StartAsync(steam, clock);
        using var browser = app.Browser();
        var portal2 = await LinkAndListAsync(app, browser);

        // The first request waits at Steam's host while the second one comes.
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        steam.ImagesBefore = () =>
        {
            reached.TrySetResult();
            return held.Task;
        };
        var first = CoverAsync(browser, portal2);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = CoverAsync(browser, portal2);
        await Task.Delay(200);
        held.SetResult();

        Assert.Equal(Jpeg, (await first).Bytes);
        Assert.Equal(Jpeg, (await second).Bytes);
        Assert.Single(steam.ImageRequests);
    }

    /// <summary>The files kept, by their path under <c>images/</c>, with <c>/</c> between folders.</summary>
    private string[] Files()
    {
        var images = Path.Combine(_data, "images");
        return
        [
            .. Directory.EnumerateFiles(images, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(images, file).Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>The start of an image's SHA-256, as a kept file's name holds it.</summary>
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];

    private sealed record Answer(byte[] Bytes, string? ContentType, string? CacheControl, EntityTagHeaderValue? Tag);

    private static async Task<Answer> CoverAsync(HttpClient browser, Guid game)
    {
        using var cover = await browser.GetAsync($"/api/v1/games/{game}/cover");
        Assert.Equal(HttpStatusCode.OK, cover.StatusCode);
        return new Answer(
            await cover.Content.ReadAsByteArrayAsync(),
            cover.Content.Headers.ContentType?.ToString(),
            cover.Headers.CacheControl?.ToString(),
            cover.Headers.ETag);
    }

    private static async Task<(string Kind, string Source, string Key, string ContentType, DateTimeOffset CheckedAt)[]> ImagesAsync(TestApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<GamesDbContext>().Images.AsNoTracking().ToListAsync();
        return [.. rows.Select(i => (i.Kind, i.Source, i.Key, i.ContentType, i.CheckedAt))];
    }

    /// <summary>The app, keeping its images in this test's directory; a sign-in outlives the months a test
    /// moves its clock by.</summary>
    private Task<TestApp> StartAsync(FakeSteam steam, TestClock clock) =>
        steam.StartAppAsync(new() { ["RESPAWN_DATA_DIR"] = _data, ["RESPAWN_SESSION_DAYS"] = "365" }, services => services.AddSingleton<TimeProvider>(clock));

    /// <summary>A Steam account linked, its library synced and its store asked where the covers are.
    /// Returns Portal 2.</summary>
    private static async Task<Guid> LinkAndListAsync(TestApp app, HttpClient browser)
    {
        await LinkAsync(app, browser);
        await Assert.Single(app.Services.GetServices<IHostedService>().OfType<SyncScheduler>()).RunDueAsync(default);
        await CoversAsync(app);
        return (await GamesAsync(browser))["Portal 2"];
    }

    private static async Task LinkAsync(TestApp app, HttpClient browser)
    {
        var owner = (await browser.RegisterAsync("first@example.org").JsonAsync()).GetProperty("id").GetGuid();
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ILibraryService>().LinkAccountAsync(owner, "steam", SteamId, null, default);
    }

    private static Task<int> CoversAsync(TestApp app) =>
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<CoverScheduler>()).RunDueAsync(default);

    private static async Task<Dictionary<string, Guid>> GamesAsync(HttpClient browser) =>
        (await browser.GetAsync("/api/v1/library?limit=500").JsonAsync()).GetProperty("games").EnumerateArray()
            .ToDictionary(game => game.GetProperty("name").GetString() ?? "", game => game.GetProperty("game_id").GetGuid());
}
