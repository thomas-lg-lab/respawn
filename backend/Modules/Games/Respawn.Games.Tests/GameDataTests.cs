using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Respawn.Games.Api;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Games.Tests.Support;
using Respawn.Testing;

namespace Respawn.Games.Tests;

/// <summary>What IGDB knows of the catalogue's games: asked on a schedule of its own, a few games a turn,
/// and kept once for the whole instance. IGDB is never called: its recorded answers are played back in
/// memory, and a turn of the schedule is run by hand.</summary>
public sealed class GameDataTests
{
    /// <summary>Portal 2, Half-Life 2 and Portal, which IGDB knows, and a game it does not.</summary>
    private static readonly string[] AppIds = ["620", "220", "400", "3999990"];

    [Fact]
    public async Task The_catalogues_games_are_asked_about_and_what_igdb_says_is_kept_once()
    {
        var (igdb, clock) = (new FakeIgdb(), new TestClock());
        await using var app = await StartAsync(igdb, clock);
        var entries = await AddGamesAsync(app, AppIds);

        Assert.Equal(1, await TurnAsync(app));

        // One question per kind, each about every game at once.
        Assert.Equal(["/v4/external_games", "/v4/games", "/v4/game_time_to_beats"], igdb.Requests.Select(request => request.Address.AbsolutePath));
        Assert.Contains("""uid = ("220","3999990","400","620")""", igdb.Requests[0].Query, StringComparison.Ordinal);
        Assert.Contains("where id = (71,72,233);", igdb.Requests[1].Query, StringComparison.Ordinal);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();

        // Which IGDB game each one is, and that IGDB was asked: the one it does not know included.
        var asked = await db.StoreEntries.AsNoTracking().ToDictionaryAsync(e => e.ExternalId, e => (e.IgdbId, e.IgdbAskedAt));
        Assert.Equal((72L, clock.Now), asked["620"]);
        Assert.Equal((233L, clock.Now), asked["220"]);
        Assert.Equal((71L, clock.Now), asked["400"]);
        Assert.Equal(((long?)null, clock.Now), asked["3999990"]);

        // What IGDB says of its games, once each.
        var games = await db.IgdbGames.AsNoTracking().ToDictionaryAsync(g => g.IgdbId);
        Assert.Equal([71L, 72L, 233L], games.Keys.Order());
        var portal2 = games[72];
        Assert.Equal(
            ("Portal 2", "portal-2", "https://www.igdb.com/games/portal-2", "co0fake72", clock.Now),
            (portal2.Name, portal2.Slug, portal2.Url, portal2.CoverImageId, portal2.ReadAt));
        Assert.Equal(["Shooter", "Puzzle", "Adventure"], portal2.Genres);
        Assert.Equal(["Science fiction", "Comedy"], portal2.Themes);
        Assert.Equal((26100, 34200, 75600, 412), (portal2.HastilySeconds, portal2.NormallySeconds, portal2.CompletelySeconds, portal2.TimeToBeatCount));
        Assert.Equal(((int?)null, (int?)null, (int?)null, (int?)null), (games[71].HastilySeconds, games[71].NormallySeconds, games[71].CompletelySeconds, games[71].TimeToBeatCount));

        // Its answers, untouched and the instance's, not an account's.
        var snapshots = (await db.RawSnapshots.AsNoTracking().ToListAsync()).OrderBy(s => s.Kind, StringComparer.Ordinal).ToList();
        Assert.Equal(["external-games", "game-time-to-beats", "games"], snapshots.Select(s => s.Kind));
        Assert.All(snapshots, snapshot => Assert.Equal(("igdb", (Guid?)null), (snapshot.Source, snapshot.LinkedAccountId)));
        Assert.Equal(FakeIgdb.Recorded("games.json"), snapshots[2].Payload);

        // The catalogue's games are left as they were: their names, and one record each.
        Assert.Equal(4, await db.Games.CountAsync());
        Assert.All(await db.Games.AsNoTracking().ToListAsync(), game => Assert.Null(game.IgdbId));
        Assert.Equal(4, entries.Count);

        Assert.Contains("game data: store steam: the game database knows 3 of the games asked about, and not 1", app.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_games_of_a_store_that_are_one_igdb_game_share_its_record_and_stay_two()
    {
        var igdb = new FakeIgdb
        {
            // Steam's 620 and 1620 are both Portal 2 for IGDB, and 400 is two of IGDB's games.
            Body = """
                [{"id":1,"game":72,"uid":"620","external_game_source":1},
                 {"id":2,"game":72,"uid":"1620","external_game_source":1},
                 {"id":3,"game":9071,"uid":"400","external_game_source":1},
                 {"id":4,"game":71,"uid":"400","external_game_source":1}]
                """,
        };
        await using var app = await StartAsync(igdb, new TestClock());
        await AddGamesAsync(app, ["620", "1620", "400"]);

        Assert.Equal(1, await TurnAsync(app));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var matched = await db.StoreEntries.AsNoTracking().ToDictionaryAsync(e => e.ExternalId, e => e.IgdbId);
        Assert.Equal(72L, matched["620"]);
        Assert.Equal(72L, matched["1620"]);
        // The smallest of the two, the same at every turn.
        Assert.Equal(71L, matched["400"]);
        Assert.Contains("where id = (71,72);", igdb.Requests[1].Query, StringComparison.Ordinal);

        // One record of Portal 2 for both, and still three games in the catalogue: nothing is merged.
        Assert.Equal([71L, 72L], (await db.IgdbGames.AsNoTracking().Select(g => g.IgdbId).ToListAsync()).Order());
        Assert.Equal(3, await db.Games.CountAsync());
        Assert.Equal(3, await db.StoreEntries.Select(e => e.GameId).Distinct().CountAsync());
    }

    [Fact]
    public async Task A_game_igdb_knows_is_asked_about_again_after_a_month_and_one_it_does_not_after_a_week()
    {
        var (igdb, clock) = (new FakeIgdb(), new TestClock());
        await using var app = await StartAsync(igdb, clock);
        await AddGamesAsync(app, AppIds);
        Assert.Equal(1, await TurnAsync(app));
        var first = clock.Now;

        // Nothing is due until then.
        clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1));
        Assert.Equal(0, await TurnAsync(app));
        Assert.Equal(3, igdb.Requests.Count);

        // A week on: the game IGDB did not know, alone. Still unknown: nothing to read of it.
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(4, igdb.Requests.Count);
        Assert.Contains("""uid = ("3999990")""", igdb.Requests[3].Query, StringComparison.Ordinal);

        // A month on: the three it knew, and what it says of them is replaced; the one it did not know
        // is asked about again with them, its week being over too.
        igdb.GamesBody = igdb.GamesBody.Replace("\"Portal 2\"", "\"Portal 2: renamed\"", StringComparison.Ordinal);
        clock.Advance(TimeSpan.FromDays(23) - TimeSpan.FromMinutes(3));
        Assert.Equal(1, await TurnAsync(app));
        Assert.Contains("""uid = ("3999990")""", igdb.Requests[4].Query, StringComparison.Ordinal);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, await TurnAsync(app));
        Assert.Contains("""uid = ("220","400","620")""", igdb.Requests[5].Query, StringComparison.Ordinal);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var portal2 = await db.IgdbGames.AsNoTracking().SingleAsync(g => g.IgdbId == 72);
        Assert.Equal(("Portal 2: renamed", clock.Now), (portal2.Name, portal2.ReadAt));
        Assert.True(clock.Now > first);
        Assert.Equal(3, await db.IgdbGames.CountAsync());
    }

    [Fact]
    public async Task A_library_is_asked_about_a_few_games_a_turn_never_asked_first()
    {
        var (igdb, clock) = (new FakeIgdb(), new TestClock());
        await using var app = await StartAsync(igdb, clock, new() { ["RESPAWN_IGDB_GAMES_PER_TURN"] = "3" });
        await AddGamesAsync(app, AppIds);

        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(0, await TurnAsync(app));

        // Three then one, in two questions about the games of the store, each game once.
        var asked = igdb.Requests.Where(request => request.Address.AbsolutePath == "/v4/external_games").Select(Uids).ToList();
        Assert.Equal([3, 1], asked.Select(ids => ids.Length));
        Assert.Equal(AppIds.Order(StringComparer.Ordinal), asked.SelectMany(ids => ids).Order(StringComparer.Ordinal));
        Assert.Contains("IGDB is asked about the games, 3 at most every few minutes", app.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_client_id_and_secret_igdb_is_never_asked_and_the_start_says_so()
    {
        var igdb = new FakeIgdb();
        await using var app = await StartAsync(igdb, new TestClock(), new()
        {
            ["RESPAWN_IGDB_CLIENT_ID"] = null,
            ["RESPAWN_IGDB_CLIENT_SECRET"] = null,
            ["RESPAWN_IGDB_CLIENT_ID_FILE"] = "",
            ["RESPAWN_IGDB_CLIENT_SECRET_FILE"] = "",
        });
        await AddGamesAsync(app, AppIds);

        Assert.Equal(0, await TurnAsync(app));

        Assert.Empty(igdb.Requests);
        Assert.Empty(igdb.TokenRequests);
        Assert.Equal(1, app.Logs.Count("no IGDB client id and secret"));
        Assert.Equal(0, app.Logs.Count("game data:"));
    }

    [Fact]
    public async Task Igdb_that_stops_answering_keeps_nothing_of_the_batch_is_said_once_and_asked_again_an_hour_later()
    {
        var (igdb, clock) = (new FakeIgdb(), new TestClock());
        await using var app = await StartAsync(igdb, clock);
        await AddGamesAsync(app, AppIds);

        // The games of the store are matched, then IGDB fails on what it knows of them.
        igdb.AnswerFor = (_, query) => query.StartsWith("fields name", StringComparison.Ordinal) ? (HttpStatusCode.TooManyRequests, "") : null;
        Assert.Equal(1, await TurnAsync(app));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            Assert.All(await db.StoreEntries.AsNoTracking().ToListAsync(), entry => Assert.Equal(((long?)null, (DateTimeOffset?)null), (entry.IgdbId, entry.IgdbAskedAt)));
            Assert.Empty(await db.IgdbGames.ToListAsync());
            Assert.Empty(await db.RawSnapshots.ToListAsync());
        }

        Assert.Equal(1, app.Logs.Count("game data: store steam could not be read (unavailable)"));

        // Left alone for an hour.
        var asked = igdb.Requests.Count;
        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(0, await TurnAsync(app));
        Assert.Equal(asked, igdb.Requests.Count);

        // Then asked again, and kept; the log says it is over.
        igdb.AnswerFor = null;
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal(1, app.Logs.Count("game data: store steam is read again"));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<GamesDbContext>().IgdbGames.CountAsync());
        }
    }

    private static Task<TestApp> StartAsync(FakeIgdb igdb, TestClock clock, Dictionary<string, string?>? settings = null) =>
        igdb.StartAppAsync(settings, services => services.AddSingleton<TimeProvider>(clock));

    /// <summary>Steam games in the catalogue, as a sync would have added them: a record and a store
    /// entry each. Returns the store entries by app id.</summary>
    private static async Task<Dictionary<string, Guid>> AddGamesAsync(TestApp app, IEnumerable<string> appIds)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var entries = new Dictionary<string, Guid>();
        foreach (var appId in appIds)
        {
            var game = new GameRow { Id = Guid.CreateVersion7(), Name = "App " + appId, CreatedAt = DateTimeOffset.UtcNow };
            var entry = new StoreEntryRow { Id = Guid.CreateVersion7(), GameId = game.Id, Source = "steam", ExternalId = appId, Name = game.Name, CreatedAt = game.CreatedAt };
            db.Games.Add(game);
            db.StoreEntries.Add(entry);
            entries[appId] = entry.Id;
        }

        await db.SaveChangesAsync();
        return entries;
    }

    /// <summary>One turn of the schedule, as the app's timer runs it. Returns how many stores' games
    /// were asked about.</summary>
    private static Task<int> TurnAsync(TestApp app) =>
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<GameDataScheduler>()).RunDueAsync(default);

    /// <summary>The Steam app ids a question about the games of the store names.</summary>
    private static string[] Uids(IgdbRequest request)
    {
        var query = request.Query;
        var start = query.IndexOf("uid = (", StringComparison.Ordinal) + "uid = (".Length;
        return query[start..query.IndexOf(')', start)].Split(',').Select(id => id.Trim('"')).ToArray();
    }
}
