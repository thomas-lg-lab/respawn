using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Igdb;
using Respawn.Games.Ports;
using Respawn.Games.Tests.Support;
using Respawn.Testing;

namespace Respawn.Games.Tests;

/// <summary>The IGDB connector: what it asks Twitch and IGDB, and what it makes of the answers. Neither
/// is ever called: their answers are recorded ones, played back in memory.</summary>
public sealed class IgdbTests : IDisposable
{
    /// <summary>The Steam games of the recorded answer, and one IGDB does not know (3999990).</summary>
    private static readonly string[] AppIds = ["620", "220", "400", "3999990", "292030", "1245620", "8930", "10"];

    private readonly string _secrets = Directory.CreateTempSubdirectory("respawn-igdb-").FullName;

    public void Dispose() => Directory.Delete(_secrets, recursive: true);

    [Fact]
    public async Task A_cover_is_asked_of_the_image_host_only_by_an_id_of_igdbs_shape_and_only_an_image_is_one()
    {
        var igdb = new FakeIgdb();
        igdb.Images["t_cover_big_2x/co0fake72.jpg"] = new(FakeSteam.RecordedBytes("cover.jpg"));
        igdb.Images["t_cover_big_2x/co0notimage.jpg"] = new("<html>not an image</html>"u8.ToArray());
        await using var app = await igdb.StartAppAsync();

        Assert.Equal("image/jpeg", (await Source(app).ReadCoverAsync("co0fake72", default))?.ContentType);
        Assert.Null(await Source(app).ReadCoverAsync("co0notimage", default));
        Assert.Null(await Source(app).ReadCoverAsync("co0missing", default));
        Assert.Null(await Source(app).ReadCoverAsync("../../other", default));
        Assert.Null(await Source(app).ReadCoverAsync("", default));

        Assert.Equal(
            ["t_cover_big_2x/co0fake72.jpg", "t_cover_big_2x/co0notimage.jpg", "t_cover_big_2x/co0missing.jpg"],
            igdb.ImageRequests.Select(request => request.Path));
        // Public images: neither the token nor Twitch is ever asked for.
        Assert.Empty(igdb.TokenRequests);
    }

    [Fact]
    public void The_image_host_follows_no_redirect()
    {
        using var transport = Igdb.DependencyInjection.ImagesTransport();

        Assert.False(transport.AllowAutoRedirect);
    }

    [Fact]
    public async Task A_recorded_answer_is_read_as_igdb_says_it()
    {
        var igdb = new FakeIgdb();
        await using var app = await igdb.StartAppAsync();
        var before = DateTimeOffset.UtcNow;

        var reading = await Source(app).MatchAsync("steam", AppIds, default);

        Assert.Equal("igdb", Source(app).Source);
        Assert.Equal("external-games", reading.Kind);
        Assert.InRange(reading.TakenAt, before, DateTimeOffset.UtcNow);
        // The answer untouched: a field this connector does not read today can be read from it later.
        Assert.Equal([FakeIgdb.Recorded("external-games.json")], reading.Payloads);
        // The game IGDB does not know is left out.
        Assert.Equal(
            [
                new GameMatch("10", "241"),
                new GameMatch("1245620", "119133"),
                new GameMatch("220", "233"),
                new GameMatch("292030", "1942"),
                new GameMatch("400", "71"),
                new GameMatch("620", "72"),
                new GameMatch("8930", "866"),
            ],
            reading.Matches.OrderBy(match => match.ExternalId, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Twitch_gives_the_token_and_igdb_is_asked_with_it_in_headers()
    {
        // Everything the HTTP client can log, which the app's own settings keep to warnings.
        var igdb = new FakeIgdb();
        await using var app = await igdb.StartAppAsync(new() { ["Logging:LogLevel:System.Net.Http.HttpClient"] = "Trace" });

        await Source(app).MatchAsync("steam", AppIds, default);

        // The client id and secret go to Twitch in a form, never in an address.
        var (tokenAddress, form) = Assert.Single(igdb.TokenRequests);
        Assert.Equal("https://id.twitch.tv/oauth2/token", tokenAddress.ToString());
        Assert.Equal(
            $"client_id={FakeIgdb.ClientId}&client_secret={FakeIgdb.ClientSecret}&grant_type=client_credentials",
            form);

        var request = Assert.Single(igdb.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.igdb.com/v4/external_games", request.Address.ToString());
        Assert.Equal(FakeIgdb.ClientId, request.ClientId);
        Assert.Equal("Bearer " + FakeIgdb.TokenOf(0), request.Authorization);
        Assert.Equal(
            """fields game,uid; where external_game_source = 1 & uid = ("10","1245620","220","292030","3999990","400","620","8930"); sort id asc; limit 500; offset 0;""",
            request.Query);

        // Neither the secret nor the token is in the log.
        Assert.Contains("external_games", app.Logs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeIgdb.ClientSecret, app.Logs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeIgdb.TokenOf(0), app.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_answer_that_sends_elsewhere_is_not_followed()
    {
        // The fake stands in for this transport, so the setting is read on the real one: followed, a
        // redirect would carry the secret or the token along.
        using var transport = Igdb.DependencyInjection.Transport();

        Assert.False(transport.AllowAutoRedirect);
    }

    [Fact]
    public async Task The_token_is_kept_until_it_nears_its_end()
    {
        var (igdb, clock) = (new FakeIgdb(), new TestClock());
        await using var app = await igdb.StartAppAsync(services: services => services.AddSingleton<TimeProvider>(clock));
        var life = TimeSpan.FromSeconds(5039234);

        await Source(app).MatchAsync("steam", AppIds, default);
        clock.Advance(life - TimeSpan.FromDays(2));
        await Source(app).MatchAsync("steam", AppIds, default);
        Assert.Single(igdb.TokenRequests);

        // Within a day of its end, a new one.
        clock.Advance(TimeSpan.FromDays(1.5));
        await Source(app).MatchAsync("steam", AppIds, default);

        Assert.Equal(2, igdb.TokenRequests.Count);
        Assert.Equal(
            ["Bearer " + FakeIgdb.TokenOf(0), "Bearer " + FakeIgdb.TokenOf(0), "Bearer " + FakeIgdb.TokenOf(1)],
            igdb.Requests.Select(request => request.Authorization));
    }

    [Fact]
    public async Task A_token_igdb_stops_taking_is_replaced_once()
    {
        var igdb = new FakeIgdb
        {
            AnswerFor = (rank, _) => rank == 0 ? (HttpStatusCode.Unauthorized, """{"message":"Authorization Failure"}""") : null,
        };
        await using var app = await igdb.StartAppAsync();

        var reading = await Source(app).MatchAsync("steam", AppIds, default);

        Assert.Equal(7, reading.Matches.Count);
        Assert.Equal(2, igdb.TokenRequests.Count);
        Assert.Equal(["Bearer " + FakeIgdb.TokenOf(0), "Bearer " + FakeIgdb.TokenOf(1)], igdb.Requests.Select(request => request.Authorization));

        // A new token refused too: IGDB refuses the instance, and is not asked a third time.
        igdb.AnswerFor = (_, _) => (HttpStatusCode.Unauthorized, """{"message":"Authorization Failure"}""");
        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).MatchAsync("steam", AppIds, default));

        Assert.Equal(SourceError.Refused, refusal.Error);
        Assert.Equal(4, igdb.Requests.Count);
        Assert.Equal(3, igdb.TokenRequests.Count);
    }

    [Fact]
    public async Task The_client_id_and_secret_are_read_again_from_their_files()
    {
        var (id, secret) = (Path.Combine(_secrets, "igdb-client-id"), Path.Combine(_secrets, "igdb-client-secret"));
        await File.WriteAllTextAsync(id, FakeIgdb.ClientId + "\n");
        var igdb = new FakeIgdb();
        await using var app = await igdb.StartAppAsync(new()
        {
            ["RESPAWN_IGDB_CLIENT_ID"] = null,
            ["RESPAWN_IGDB_CLIENT_SECRET"] = null,
            ["RESPAWN_IGDB_CLIENT_ID_FILE"] = id,
            ["RESPAWN_IGDB_CLIENT_SECRET_FILE"] = secret,
        });

        // The secret is not there yet: nothing is asked.
        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).MatchAsync("steam", AppIds, default));
        Assert.Equal(SourceError.NotConfigured, refusal.Error);
        Assert.Empty(igdb.TokenRequests);

        await File.WriteAllTextAsync(secret, FakeIgdb.ClientSecret);
        await Source(app).MatchAsync("steam", AppIds, default);
        Assert.Contains($"client_secret={FakeIgdb.ClientSecret}&", Assert.Single(igdb.TokenRequests).Form, StringComparison.Ordinal);

        // Another secret, and the token of the one before is not used with it.
        await File.WriteAllTextAsync(secret, "another0secret0nobodys0secret0");
        await Source(app).MatchAsync("steam", AppIds, default);
        Assert.Contains("client_secret=another0secret0nobodys0secret0&", igdb.TokenRequests[1].Form, StringComparison.Ordinal);
        Assert.Equal("Bearer " + FakeIgdb.TokenOf(1), igdb.Requests[1].Authorization);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("not a client id", FakeIgdb.ClientSecret)]
    [InlineData(FakeIgdb.ClientId, "secret&grant_type=other")]
    public async Task Without_a_client_id_and_secret_nothing_is_asked(string? id, string? secret)
    {
        var igdb = new FakeIgdb();
        await using var app = await igdb.StartAppAsync(new()
        {
            ["RESPAWN_IGDB_CLIENT_ID"] = id,
            ["RESPAWN_IGDB_CLIENT_SECRET"] = secret,
            ["RESPAWN_IGDB_CLIENT_ID_FILE"] = Path.Combine(_secrets, "none"),
            ["RESPAWN_IGDB_CLIENT_SECRET_FILE"] = Path.Combine(_secrets, "none"),
        });

        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).MatchAsync("steam", AppIds, default));

        Assert.Equal(SourceError.NotConfigured, refusal.Error);
        Assert.Empty(igdb.TokenRequests);
        Assert.Empty(igdb.Requests);
        if (secret is { Length: > 0 })
        {
            Assert.DoesNotContain(secret, refusal.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "token-wrong-secret.json", SourceError.Refused)]
    [InlineData(HttpStatusCode.BadRequest, "token-unknown-client.json", SourceError.Refused)]
    [InlineData(HttpStatusCode.ServiceUnavailable, null, SourceError.Unavailable)]
    [InlineData(HttpStatusCode.OK, null, SourceError.Unreadable)]
    public async Task What_twitch_refuses_is_said(HttpStatusCode status, string? recorded, SourceError error)
    {
        var igdb = new FakeIgdb
        {
            TokenStatus = status,
            // A 200 that holds no token.
            TokenBody = recorded is null ? """{"token_type":"bearer"}""" : FakeIgdb.Recorded(recorded),
        };
        await using var app = await igdb.StartAppAsync();

        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).MatchAsync("steam", AppIds, default));

        Assert.Equal(error, refusal.Error);
        Assert.Empty(igdb.Requests);
        Assert.DoesNotContain(FakeIgdb.ClientSecret, refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "[]", SourceError.Refused)]
    [InlineData(HttpStatusCode.TooManyRequests, "", SourceError.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, "", SourceError.Unavailable)]
    [InlineData(HttpStatusCode.NotFound, "", SourceError.Unreadable)]
    [InlineData(HttpStatusCode.OK, "not json", SourceError.Unreadable)]
    [InlineData(HttpStatusCode.OK, """{"message":"a list was expected"}""", SourceError.Unreadable)]
    public async Task What_igdb_refuses_is_said(HttpStatusCode status, string body, SourceError error)
    {
        var igdb = new FakeIgdb { Status = status, Body = body };
        await using var app = await igdb.StartAppAsync();

        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).MatchAsync("steam", AppIds, default));

        Assert.Equal(error, refusal.Error);
    }

    [Fact]
    public async Task A_row_whose_game_is_not_a_number_says_nothing()
    {
        var igdb = new FakeIgdb
        {
            Body = """
                [{"id":1,"game":null,"uid":"620","external_game_source":1},
                 {"id":2,"game":{"id":72},"uid":"620","external_game_source":1},
                 {"id":3,"game":"233","uid":"220","external_game_source":1},
                 {"id":4,"game":71,"uid":"400","external_game_source":1}]
                """,
        };
        await using var app = await igdb.StartAppAsync();

        var reading = await Source(app).MatchAsync("steam", AppIds, default);

        Assert.Equal([new GameMatch("400", "71")], reading.Matches);
    }

    [Theory]
    [InlineData("""{"access_token":"fake0token0nobodys0token000000","expires_in":"5039234"}""")]
    [InlineData("""{"access_token":"fake0token0nobodys0token000000","expires_in":null}""")]
    public async Task A_token_whose_life_is_not_a_number_is_unreadable(string body)
    {
        var igdb = new FakeIgdb { TokenBody = body };
        await using var app = await igdb.StartAppAsync();

        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).MatchAsync("steam", AppIds, default));

        Assert.Equal(SourceError.Unreadable, refusal.Error);
        Assert.Empty(igdb.Requests);
    }

    [Fact]
    public async Task A_question_given_up_while_it_waits_gives_its_turn_back()
    {
        var igdb = new FakeIgdb();
        await using var app = await igdb.StartAppAsync();
        await Source(app).MatchAsync("steam", AppIds, default);
        var watch = Stopwatch.StartNew();

        // The second waits for its turn, a quarter of a second after the first, and gives up before.
        using var giveUp = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source(app).MatchAsync("steam", AppIds, giveUp.Token));
        await Source(app).MatchAsync("steam", AppIds, default);
        watch.Stop();

        // The third takes the turn the second gave back, not the one after it.
        Assert.Equal(2, igdb.Requests.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(400), $"the third question waited {watch.Elapsed}");
    }

    [Fact]
    public async Task Igdb_out_of_reach_may_be_asked_again()
    {
        var igdb = new FakeIgdb { Unreachable = true };
        await using var app = await igdb.StartAppAsync();

        var refusal = await Assert.ThrowsAsync<SourceException>(() => Source(app).MatchAsync("steam", AppIds, default));

        Assert.Equal(SourceError.Unavailable, refusal.Error);
    }

    [Fact]
    public async Task A_long_answer_is_read_page_after_page_at_igdbs_pace()
    {
        // 500 games asked, each one IGDB game, and three of them a second one: 503 rows, two pages.
        var ids = Enumerable.Range(1, 500).Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
        var igdb = new FakeIgdb
        {
            AnswerFor = (_, query) => (HttpStatusCode.OK, query.EndsWith("offset 0;", StringComparison.Ordinal)
                ? Rows(ids.Select(id => (id, id)))
                : Rows([("1", "9001"), ("2", "9002"), ("3", "9003")])),
        };
        await using var app = await igdb.StartAppAsync();

        var watch = Stopwatch.StartNew();
        var reading = await Source(app).MatchAsync("steam", ids, default);
        watch.Stop();

        Assert.Equal(["offset 0;", "offset 500;"], igdb.Requests.Select(request => request.Query[request.Query.LastIndexOf("offset", StringComparison.Ordinal)..]));
        Assert.Equal(2, reading.Payloads.Count);
        Assert.Equal(503, reading.Matches.Count);
        Assert.Equal(["1", "9001"], reading.Matches.Where(match => match.ExternalId == "1").Select(match => match.GameId));
        // Four questions a second at most: the second waits a quarter of a second after the first.
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(200), $"two questions in {watch.Elapsed}");
    }

    [Fact]
    public async Task What_cannot_be_a_steam_game_is_not_sent()
    {
        var igdb = new FakeIgdb();
        await using var app = await igdb.StartAppAsync();

        await Source(app).MatchAsync("steam", ["620", "abc", "-1", " 220", "99999999999", "4294967296", "620\"); fields *; (\""], default);

        Assert.Contains("""uid = ("620");""", Assert.Single(igdb.Requests).Query, StringComparison.Ordinal);

        // None left: nothing is asked, not even a token.
        igdb.TokenRequests.Clear();
        var reading = await Source(app).MatchAsync("steam", ["abc"], default);
        Assert.Empty(reading.Payloads);
        Assert.Empty(reading.Matches);
        Assert.Single(igdb.Requests);
        Assert.Empty(igdb.TokenRequests);
    }

    [Fact]
    public async Task Only_the_stores_igdb_is_asked_about_and_500_games_at_once()
    {
        var igdb = new FakeIgdb();
        await using var app = await igdb.StartAppAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Source(app).MatchAsync("gog", ["1"], default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Source(app).MatchAsync("steam", [.. Enumerable.Range(1, 501).Select(id => id.ToString(CultureInfo.InvariantCulture))], default));
        Assert.Empty(igdb.Requests);
    }

    [Fact]
    public async Task Igdbs_games_and_how_long_they_take_are_read_as_igdb_says_them()
    {
        var igdb = new FakeIgdb();
        await using var app = await igdb.StartAppAsync();

        var reading = await Source(app).ReadGamesAsync(["233", "72", "71"], default);

        // Two questions, each about every game at once.
        Assert.Equal(
            [
                ("https://api.igdb.com/v4/games", "fields name,slug,url,cover.image_id,genres.name,themes.name,summary,first_release_date,similar_games,game_type,version_parent,parent_game; where id = (71,72,233); sort id asc; limit 500;"),
                ("https://api.igdb.com/v4/game_time_to_beats", "fields game_id,hastily,normally,completely,count; where game_id = (71,72,233); sort id asc; limit 500;"),
            ],
            igdb.Requests.Select(request => (request.Address.ToString(), request.Query)));

        // The answers untouched, to be kept.
        Assert.Equal(
            [("games", FakeIgdb.Recorded("games.json")), ("game-time-to-beats", FakeIgdb.Recorded("game-time-to-beats.json"))],
            reading.Answers.Select(answer => (answer.Kind, answer.Payload)));

        var games = reading.Games.ToDictionary(game => game.GameId);
        Assert.Equal(["233", "71", "72"], games.Keys.Order(StringComparer.Ordinal));
        var portal2 = games["72"];
        Assert.Equal(
            ("Portal 2", "portal-2", "https://www.igdb.com/games/portal-2", "co0fake72"),
            (portal2.Name, portal2.Slug, portal2.Url, portal2.CoverImageId));
        Assert.Equal(["Shooter", "Puzzle", "Adventure"], portal2.Genres);
        Assert.Equal(["Science fiction", "Comedy"], portal2.Themes);
        Assert.StartsWith("More test chambers", portal2.Summary, StringComparison.Ordinal);
        Assert.Equal(new TimeToBeat(26100, 34200, 75600, 412), portal2.TimeToBeat);
        // The day it came out, from IGDB's moment in UTC; the games it lists as similar, in its order.
        Assert.Equal(new DateOnly(2011, 4, 18), portal2.ReleasedOn);
        Assert.Equal(["1020", "71", "233"], portal2.Similar);
        Assert.Null(games["233"].ReleasedOn);
        Assert.Empty(games["233"].Similar ?? []);

        // Some figures said, others not; and a game nobody said anything of.
        Assert.Equal(new TimeToBeat(39600, 50400, null, 3), games["233"].TimeToBeat);
        Assert.Null(games["71"].TimeToBeat);
    }

    [Fact]
    public async Task What_igdb_gives_in_another_shape_is_left_out()
    {
        var igdb = new FakeIgdb
        {
            GamesBody = """
                [7,
                 {"id":71,"name":42,"slug":"  ","url":"https://elsewhere.example.org/games/portal","cover":"co0fake71",
                  "genres":[{"name":"Puzzle"},"Shooter",{"name":"Puzzle"},{"name":null}],"themes":{"name":"Action"},"summary":null,
                  "first_release_date":"1191974400","similar_games":[72,"233",-5,0,72,{"id":233},1.5]},
                 {"id":72,"url":"https://www.igdb.com/games/portal-2","first_release_date":-86400,"similar_games":{"id":71}},
                 {"id":72,"name":"Portal 2 again"},
                 {"id":"233","name":"Half-Life 2"},
                 {"id":999,"name":"Not asked"}]
                """,
            TimesBody = """
                [{"game_id":71,"hastily":-1,"normally":"3600","completely":null,"count":5},
                 {"game_id":72,"normally":9999999999,"count":1},
                 {"game_id":72,"normally":3600,"count":-2}]
                """,
        };
        await using var app = await igdb.StartAppAsync();

        var reading = await Source(app).ReadGamesAsync(["71", "72", "233"], default);

        var games = reading.Games.ToDictionary(game => game.GameId);
        Assert.Equal(["71", "72"], games.Keys.Order(StringComparer.Ordinal));
        // A link that leads elsewhere, words of another kind, and blanks: nothing.
        var portal = games["71"];
        Assert.All(new[] { portal.Name, portal.Slug, portal.Url, portal.CoverImageId, portal.Summary }, value => Assert.Null(value));
        Assert.Equal(["Puzzle"], portal.Genres);
        Assert.Empty(portal.Themes);
        Assert.Null(portal.TimeToBeat);
        // A date that is not a number is none; of the similar games, only ids, each once.
        Assert.Null(portal.ReleasedOn);
        Assert.Equal(["72"], portal.Similar);
        // A moment before 1970 is a day all the same; a list that is not one is none.
        Assert.Equal(new DateOnly(1969, 12, 31), games["72"].ReleasedOn);
        Assert.Empty(games["72"].Similar ?? []);
        // A game is read once, from its first row; a duration once, from the first row that says one.
        Assert.Equal(("https://www.igdb.com/games/portal-2", (string?)null), (games["72"].Url, games["72"].Name));
        Assert.Equal(new TimeToBeat(null, 3600, null, null), games["72"].TimeToBeat);
    }

    [Fact]
    public async Task What_cannot_be_an_igdb_game_is_not_sent()
    {
        var igdb = new FakeIgdb();
        await using var app = await igdb.StartAppAsync();

        await Source(app).ReadGamesAsync(["72", "abc", "-1", "0", " 71", "72); fields *; (1"], default);

        Assert.All(igdb.Requests, request => Assert.Contains("= (72);", request.Query, StringComparison.Ordinal));
        Assert.Equal(2, igdb.Requests.Count);

        // None left: nothing is asked, not even a token.
        igdb.TokenRequests.Clear();
        var reading = await Source(app).ReadGamesAsync(["abc"], default);
        Assert.Empty(reading.Answers);
        Assert.Equal(2, igdb.Requests.Count);
        Assert.Empty(igdb.TokenRequests);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Source(app).ReadGamesAsync([.. Enumerable.Range(1, 501).Select(id => id.ToString(CultureInfo.InvariantCulture))], default));
    }

    /// <summary>An answer of <c>external_games</c> with these rows, Steam's id then IGDB's.</summary>
    private static string Rows(IEnumerable<(string Uid, string Game)> rows) =>
        "[" + string.Join(',', rows.Select((row, rank) => string.Create(
            CultureInfo.InvariantCulture, $$"""{"id":{{rank + 1}},"game":{{row.Game}},"uid":"{{row.Uid}}","external_game_source":1}"""))) + "]";

    private static IGameDataSource Source(TestApp app) => app.Services.GetRequiredService<IGameDataSource>();
}
