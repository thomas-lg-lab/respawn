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

        var reading = await Source(app).ReadLibraryAsync(SteamId, null, default);

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
        // Everything the HTTP client can log, which the app's own settings keep to warnings.
        var steam = new FakeSteam();
        await using var app = await steam.StartAppAsync(new() { ["Logging:LogLevel:System.Net.Http.HttpClient"] = "Trace" });

        await Source(app).ReadLibraryAsync(SteamId, null, default);

        var (address, key) = Assert.Single(steam.Requests);
        Assert.Equal("https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/", address.GetLeftPart(UriPartial.Path));
        Assert.Equal(
            ["steamid=" + SteamId, "include_appinfo=true", "include_played_free_games=true", "include_free_sub=true", "skip_unvetted_apps=false", "format=json"],
            address.Query.TrimStart('?').Split('&'));

        // The key is in the header Steam reads it from, never in the address, and never in the log.
        Assert.Equal(FakeSteam.Key, key);
        Assert.DoesNotContain(FakeSteam.Key, address.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GetOwnedGames", app.Logs.Text, StringComparison.Ordinal);
        Assert.Contains("x-webapi-key", app.Logs.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeSteam.Key, app.Logs.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SteamId, app.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_answer_that_sends_elsewhere_is_not_followed()
    {
        // The fake stands in for this transport, so the setting is read on the real one: followed, a
        // redirect would carry the key's header along.
        using var transport = Steam.DependencyInjection.Transport();

        Assert.False(transport.AllowAutoRedirect);
    }

    [Fact]
    public async Task What_steam_counts_wrong_or_names_oddly_does_not_cost_the_library()
    {
        // A negative playtime is not said; a name keeps no control character, which the database refuses.
        var steam = new FakeSteam
        {
            Body = """{"response":{"game_count":2,"games":[{"appid":620,"name":"Por\u0000tal\u0009 2","playtime_forever":-5,"playtime_2weeks":-1},{"appid":400,"name":"Portal","playtime_forever":30}]}}""",
        };
        await using var app = await steam.StartAppAsync();
        using var browser = app.Browser();
        var owner = (await browser.RegisterAsync().JsonAsync()).GetProperty("id").GetGuid();
        var account = await UseAsync(app, library => library.LinkAccountAsync(owner, "steam", SteamId, null, default));

        var reading = await Source(app).ReadLibraryAsync(SteamId, null, default);
        await UseAsync(app, library => library.RecordLibraryAsync(account.Id, reading, daily: true, default));

        Assert.Equal((null, null), (reading.Items[0].Playtime, reading.Items[0].RecentPlaytime));
        var games = (await browser.GetAsync("/api/v1/library").JsonAsync()).GetProperty("games");
        Assert.Equal(["Portal", "Portal 2"], games.EnumerateArray().Select(game => game.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task The_answer_is_read_as_utf_8_whatever_it_says_it_is()
    {
        var steam = new FakeSteam
        {
            ContentType = "application/json; charset=bogus-8",
            Body = """{"response":{"game_count":1,"games":[{"appid":620,"name":"Pokémon ポータル"}]}}""",
        };
        await using var app = await steam.StartAppAsync();

        var reading = await Source(app).ReadLibraryAsync(SteamId, null, default);
        Assert.Equal("Pokémon ポータル", Assert.Single(reading.Items).Name);
        Assert.Equal(steam.Body, reading.Payload);

        // Bytes that are not UTF-8 are refused, not replaced: the payload is kept as it came.
        steam.Bytes = [0x7B, 0xFF, 0xFE, 0x7D];
        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadLibraryAsync(SteamId, null, default));
        Assert.Equal(SourceError.Unreadable, refusal.Error);
    }

    [Fact]
    public async Task An_answer_too_large_is_not_worth_another_try()
    {
        var steam = new FakeSteam { Bytes = new byte[(16 * 1024 * 1024) + 1] };
        await using var app = await steam.StartAppAsync();

        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadLibraryAsync(SteamId, null, default));

        Assert.Equal(SourceError.Unreadable, refusal.Error);
    }

    [Fact]
    public async Task A_private_profile_is_a_reading_that_lists_nothing_and_vouches_for_nothing()
    {
        var steam = new FakeSteam { Body = FakeSteam.Recorded("owned-games-private.json") };
        await using var app = await steam.StartAppAsync();

        var reading = await Source(app).ReadLibraryAsync(SteamId, null, default);

        Assert.Empty(reading.Items);
        Assert.False(reading.Complete);
        Assert.Equal(steam.Body, reading.Payload);
    }

    [Fact]
    public async Task A_library_steam_counts_otherwise_than_it_lists_is_not_taken_as_whole()
    {
        var steam = new FakeSteam { Body = """{"response":{"game_count":3,"games":[{"appid":620,"playtime_forever":10}]}}""" };
        await using var app = await steam.StartAppAsync();

        var reading = await Source(app).ReadLibraryAsync(SteamId, null, default);

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

        var reading = await Source(app).ReadLibraryAsync(account.ExternalId, null, default);
        await UseAsync(app, library => library.RecordLibraryAsync(account.Id, reading, daily: true, default));

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
    public async Task A_key_handed_over_with_the_request_is_used_in_place_of_the_instances()
    {
        // Nothing hands one over yet: the port has room for an owner's own key.
        const string Own = "FEDCBA9876543210FEDCBA9876543210";
        var steam = new FakeSteam();
        var nowhere = Path.Combine(Path.GetTempPath(), $"respawn-no-steam-key-{Guid.NewGuid():N}");
        await using var app = await steam.StartAppAsync();
        await using var keyless = await steam.StartAppAsync(new() { ["RESPAWN_STEAM_API_KEY"] = null, ["RESPAWN_STEAM_API_KEY_FILE"] = nowhere });

        await Source(app).ReadLibraryAsync(SteamId, null, default);
        await Source(app).ReadLibraryAsync(SteamId, Own, default);
        // An instance without a key of its own reads with the one it is handed.
        await Source(keyless).ReadLibraryAsync(SteamId, Own + "\n", default);

        Assert.Equal([FakeSteam.Key, Own, Own], steam.Requests.Select(request => request.Key));

        // What cannot be a key is not sent anywhere, and the instance's is not used in its place.
        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadLibraryAsync(SteamId, "not a key\r\nx-other: header", default));
        Assert.Equal(SourceError.NotConfigured, refusal.Error);
        Assert.DoesNotContain("x-other", refusal.ToString(), StringComparison.Ordinal);
        Assert.Equal(3, steam.Requests.Count);
    }

    [Fact]
    public async Task Without_a_key_steam_is_not_asked()
    {
        var steam = new FakeSteam();
        var nowhere = Path.Combine(Path.GetTempPath(), $"respawn-no-steam-key-{Guid.NewGuid():N}");
        await using var app = await steam.StartAppAsync(new() { ["RESPAWN_STEAM_API_KEY"] = null, ["RESPAWN_STEAM_API_KEY_FILE"] = nowhere });

        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadLibraryAsync(SteamId, null, default));

        Assert.Equal(SourceError.NotConfigured, refusal.Error);
        Assert.Empty(steam.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\0")]
    public async Task A_key_file_setting_that_names_no_file_is_no_key(string file)
    {
        // An empty line in a compose file: the instance's setting, never taken for a wrong account id.
        var steam = new FakeSteam();
        await using var app = await steam.StartAppAsync(new() { ["RESPAWN_STEAM_API_KEY"] = null, ["RESPAWN_STEAM_API_KEY_FILE"] = file });

        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadLibraryAsync(SteamId, null, default));

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
            await Source(app).ReadLibraryAsync(SteamId, null, default);
            await File.WriteAllTextAsync(file, "SECONDKEY0000000000000000000000B\n");
            await Source(app).ReadLibraryAsync(SteamId, null, default);

            Assert.Equal(["FIRSTKEY00000000000000000000000A", "SECONDKEY0000000000000000000000B"], steam.Requests.Select(request => request.Key));

            // What cannot be a key is not sent anywhere, and is said without being quoted.
            await File.WriteAllTextAsync(file, "not a key\r\nx-other: header");
            var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadLibraryAsync(SteamId, null, default));
            Assert.Equal(SourceError.NotConfigured, refusal.Error);
            Assert.Contains("is not one", refusal.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("x-other", refusal.ToString(), StringComparison.Ordinal);
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

        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadLibraryAsync(SteamId, null, default));

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

        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadLibraryAsync(SteamId, null, default));

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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source(app).ReadLibraryAsync(SteamId, null, givenUp.Token));
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

        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).ReadLibraryAsync(SteamId, null, default));

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

        await Assert.ThrowsAsync<ArgumentException>(() => Source(app).ReadLibraryAsync(id, null, default));

        Assert.Empty(steam.Requests);
    }

    [Fact]
    public async Task The_app_starts_and_answers_with_no_steam_key()
    {
        // The runtime rule: nothing is required to start. The connector is there, and has asked for nothing.
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
