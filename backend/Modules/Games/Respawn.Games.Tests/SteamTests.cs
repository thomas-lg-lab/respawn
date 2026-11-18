using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Ports;
using Respawn.Games.Tests.Support;
using Respawn.Testing;

namespace Respawn.Games.Tests;

/// <summary>The Steam connector: what it asks Steam, and what it makes of the answers. Steam is never
/// called: its answers are recorded ones, played back in memory.</summary>
public sealed class SteamTests
{
    private const string SteamId = "76561190000000001";

    [Fact]
    public async Task A_recorded_library_is_read_as_steam_says_it()
    {
        var steam = new FakeSteam();
        await using var app = await steam.StartAppAsync();
        var before = DateTimeOffset.UtcNow;

        var reading = await Source(app).ReadLibraryAsync(SteamId, default);

        Assert.Equal("owned-games", reading.Kind);
        Assert.InRange(reading.TakenAt, before, DateTimeOffset.UtcNow);
        // The answer untouched: a field this connector does not read today can be read from it later.
        Assert.Equal(FakeSteam.Recorded("owned-games.json"), reading.Payload);
        Assert.True(reading.Complete);

        Assert.Equal(
            [
                // Minutes become durations, seconds since 1970 become dates.
                new OwnedItem("620", "Portal 2", TimeSpan.FromMinutes(1843), DateTimeOffset.FromUnixTimeSeconds(1793478600), TimeSpan.FromMinutes(95)),
                // Not played lately: Steam leaves the figure out, and it is not said.
                new OwnedItem("220", "Half-Life 2", TimeSpan.FromMinutes(734), DateTimeOffset.FromUnixTimeSeconds(1352742600)),
                // Never launched: a date of zero is no date.
                new OwnedItem("400", "Portal", TimeSpan.Zero, null),
                // A game Steam gives no name for.
                new OwnedItem("3999990", null, TimeSpan.FromMinutes(7), DateTimeOffset.FromUnixTimeSeconds(1790000000)),
            ],
            reading.Items);
    }

    [Fact]
    public async Task Steam_is_asked_for_the_whole_library_with_the_key_in_a_header()
    {
        var steam = new FakeSteam();
        await using var app = await steam.StartAppAsync();

        await Source(app).ReadLibraryAsync(SteamId, default);

        var (address, key) = Assert.Single(steam.Requests);
        Assert.Equal("https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/", address.GetLeftPart(UriPartial.Path));
        Assert.Equal(
            ["steamid=" + SteamId, "include_appinfo=true", "include_played_free_games=true", "include_free_sub=true", "skip_unvetted_apps=false", "format=json"],
            address.Query.TrimStart('?').Split('&'));

        // The key is in the header Steam reads it from, never in the address, and never in the log.
        Assert.Equal(FakeSteam.Key, key);
        Assert.DoesNotContain(FakeSteam.Key, address.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeSteam.Key, app.Logs.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_private_profile_is_a_reading_that_lists_nothing_and_vouches_for_nothing()
    {
        var steam = new FakeSteam { Body = FakeSteam.Recorded("owned-games-private.json") };
        await using var app = await steam.StartAppAsync();

        var reading = await Source(app).ReadLibraryAsync(SteamId, default);

        Assert.Empty(reading.Items);
        Assert.False(reading.Complete);
        Assert.Equal(steam.Body, reading.Payload);
    }

    [Fact]
    public async Task A_library_steam_counts_otherwise_than_it_lists_is_not_taken_as_whole()
    {
        var steam = new FakeSteam { Body = """{"response":{"game_count":3,"games":[{"appid":620,"playtime_forever":10}]}}""" };
        await using var app = await steam.StartAppAsync();

        var reading = await Source(app).ReadLibraryAsync(SteamId, default);

        Assert.Equal("620", Assert.Single(reading.Items).ExternalId);
        Assert.False(reading.Complete);
    }

    [Fact]
    public async Task A_library_read_from_steam_is_the_library_the_api_answers()
    {
        var steam = new FakeSteam();
        await using var app = await steam.StartAppAsync();
        using var browser = app.Browser();
        var owner = (await browser.RegisterAsync().JsonAsync()).GetProperty("id").GetGuid();
        var account = await UseAsync(app, library => library.LinkAccountAsync(owner, Source(app).Source, SteamId, "player", default));

        var reading = await Source(app).ReadLibraryAsync(account.ExternalId, default);
        await UseAsync(app, library => library.RecordLibraryAsync(account.Id, reading, default));

        var games = (await browser.GetAsync("/api/v1/library?sort=playtime").JsonAsync()).GetProperty("games");
        Assert.Equal(
            [("Portal 2", 1843 * 60L, (long?)95 * 60), ("Half-Life 2", 734 * 60L, null), (null, 7 * 60L, null), ("Portal", 0L, null)],
            games.EnumerateArray().Select(game => (
                game.GetProperty("name").GetString(),
                game.GetProperty("playtime_seconds").GetInt64(),
                game.GetProperty("recent_playtime_seconds") is { ValueKind: JsonValueKind.Number } recent ? recent.GetInt64() : (long?)null)));
        Assert.All(games.EnumerateArray(), game =>
            Assert.Equal("steam", Assert.Single(game.GetProperty("entries").EnumerateArray()).GetProperty("source").GetString()));
    }

    [Fact]
    public async Task Without_a_key_steam_is_not_asked()
    {
        var steam = new FakeSteam();
        var nowhere = Path.Combine(Path.GetTempPath(), $"respawn-no-steam-key-{Guid.NewGuid():N}");
        await using var app = await steam.StartAppAsync(new() { ["RESPAWN_STEAM_API_KEY"] = null, ["RESPAWN_STEAM_API_KEY_FILE"] = nowhere });

        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadLibraryAsync(SteamId, default));

        Assert.Equal(SourceError.NotConfigured, refusal.Error);
        Assert.Empty(steam.Requests);
    }

    [Fact]
    public async Task The_key_is_read_from_a_file_again_at_each_reading()
    {
        var steam = new FakeSteam();
        var file = Path.Combine(Path.GetTempPath(), $"respawn-steam-key-{Guid.NewGuid():N}");
        try
        {
            await using var app = await steam.StartAppAsync(new() { ["RESPAWN_STEAM_API_KEY"] = null, ["RESPAWN_STEAM_API_KEY_FILE"] = file });

            // The file appears after the app has started, with the line break an editor leaves.
            await File.WriteAllTextAsync(file, "FIRSTKEY00000000000000000000000A\n");
            await Source(app).ReadLibraryAsync(SteamId, default);
            await File.WriteAllTextAsync(file, "SECONDKEY0000000000000000000000B\n");
            await Source(app).ReadLibraryAsync(SteamId, default);

            Assert.Equal(["FIRSTKEY00000000000000000000000A", "SECONDKEY0000000000000000000000B"], steam.Requests.Select(request => request.Key));

            // What cannot be a key is not sent anywhere.
            await File.WriteAllTextAsync(file, "not a key\r\nx-other: header");
            var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadLibraryAsync(SteamId, default));
            Assert.Equal(SourceError.NotConfigured, refusal.Error);
            Assert.Equal(2, steam.Requests.Count);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, SourceError.Refused)]
    [InlineData(HttpStatusCode.Forbidden, SourceError.Refused)]
    [InlineData(HttpStatusCode.TooManyRequests, SourceError.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, SourceError.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, SourceError.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, SourceError.Unreadable)]
    [InlineData(HttpStatusCode.Found, SourceError.Unreadable)]
    public async Task A_refusal_of_steam_says_whether_trying_again_may_work(HttpStatusCode status, SourceError expected)
    {
        // Steam answers a refusal with a page, not with JSON.
        var steam = new FakeSteam { Status = status, Body = "<html><body><h1>Refused</h1></body></html>" };
        await using var app = await steam.StartAppAsync();

        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadLibraryAsync(SteamId, default));

        Assert.Equal(expected, refusal.Error);
        Assert.DoesNotContain(FakeSteam.Key, refusal.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Steam_not_answering_is_worth_another_try(bool timesOut, bool unreachable)
    {
        var steam = new FakeSteam { TimesOut = timesOut, Unreachable = unreachable };
        await using var app = await steam.StartAppAsync();

        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadLibraryAsync(SteamId, default));

        Assert.Equal(SourceError.Unavailable, refusal.Error);
        Assert.DoesNotContain(FakeSteam.Key, refusal.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_reading_given_up_by_its_caller_is_not_a_failure_of_steam()
    {
        var steam = new FakeSteam { TimesOut = true };
        await using var app = await steam.StartAppAsync();
        using var givenUp = new CancellationTokenSource();
        await givenUp.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source(app).ReadLibraryAsync(SteamId, givenUp.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"games":[]}""")]
    [InlineData("""{"response":[]}""")]
    [InlineData("""{"response":{"games":{}}}""")]
    [InlineData("""{"response":{"games":[{"name":"No id"}]}}""")]
    [InlineData("""{"response":{"games":[{"appid":"620"}]}}""")]
    [InlineData("""{"response":{"games":[{"appid":-1}]}}""")]
    [InlineData("""{"response":{"games":[{"appid":620,"playtime_forever":"long"}]}}""")]
    [InlineData("""{"response":{"games":[{"appid":620,"rtime_last_played":99999999999999999}]}}""")]
    public async Task An_answer_that_is_not_a_library_is_refused_whole(string body)
    {
        var steam = new FakeSteam { Body = body };
        await using var app = await steam.StartAppAsync();

        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadLibraryAsync(SteamId, default));

        Assert.Equal(SourceError.Unreadable, refusal.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("player")]
    [InlineData("7656119000000000")]
    [InlineData("765611900000000011")]
    [InlineData("7656119000000000a")]
    [InlineData("76561190000000001&key=x")]
    [InlineData("٧٦٥٦١١٩٠٠٠٠٠٠٠٠٠١")]
    public async Task Only_a_steam_id_is_ever_put_in_the_address(string id)
    {
        var steam = new FakeSteam();
        await using var app = await steam.StartAppAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Source(app).ReadLibraryAsync(id, default));

        Assert.Empty(steam.Requests);
    }

    [Fact]
    public async Task The_app_starts_and_answers_with_no_steam_key()
    {
        // The runtime rule: nothing is required to start, and no secret is read before it is needed.
        await using var app = TestApp.Offline();
        using var browser = app.Browser();

        Assert.Equal(HttpStatusCode.OK, await browser.GetAsync("/healthz").StatusAsync());
        Assert.Equal("steam", Source(app).Source);
    }

    private static ILibrarySource Source(TestApp app) =>
        Assert.Single(app.Services.GetServices<ILibrarySource>(), source => source.Source == "steam");

    /// <summary>A use case, called as a scheduler will: in a scope of its own.</summary>
    private static async Task<T> UseAsync<T>(TestApp app, Func<ILibraryService, Task<T>> use)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<ILibraryService>());
    }
}
