using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Api;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Games.Tests.Support;
using Respawn.Testing;
using static Respawn.Games.Tests.Support.Fixtures;

namespace Respawn.Games.Tests;

/// <summary>The tags Steam's players gave the catalogue's games: asked of Steam's store on a schedule of their
/// own, a few games a turn, with the tags' names in each language of the app, and kept once for the whole
/// instance. Steam answers from memory, and a turn of the schedule is run by hand.</summary>
public sealed class StoreTagTests
{
    /// <summary>Portal 2 and Half-Life 2, which have tags; Portal, which the store knows with none; and a
    /// game the store does not know.</summary>
    private static readonly string[] AppIds = ["620", "220", "400", "3999990"];

    [Fact]
    public async Task A_turn_asks_the_names_then_the_tags_of_the_stores_games_and_keeps_them_once()
    {
        var (steam, clock) = (Store(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        var entries = await AddGamesAsync(app, AppIds);

        Assert.Equal(1, await TagsAsync(app));

        // The names in English and French, then the tags of every game at once; never with a key.
        Assert.Equal(
            ["/IStoreService/GetTagList/v1/", "/IStoreService/GetTagList/v1/", "/IStoreBrowseService/GetItems/v1/"],
            steam.StoreRequests.Select(request => request.Address.AbsolutePath));
        Assert.Equal(["?language=english", "?language=french"], steam.StoreRequests.Take(2).Select(request => request.Address.Query));
        var asked = Uri.UnescapeDataString(steam.StoreRequests[2].Address.Query);
        Assert.All(AppIds, appId => Assert.Contains($"{{\"appid\":{appId}}}", asked, StringComparison.Ordinal));
        Assert.Contains("\"include_tag_count\":20", asked, StringComparison.Ordinal);
        Assert.All(steam.StoreRequests, request => Assert.Null(request.Key));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();

        // Each game's tags in the store's order, the heaviest first; none for the games without, and every
        // game marked as asked.
        var tags = (await db.StoreEntryTags.AsNoTracking().ToListAsync())
            .GroupBy(tag => tag.StoreEntryId)
            .ToDictionary(game => game.Key, game => game.OrderBy(tag => tag.Rank).Select(tag => tag.TagId).ToArray());
        Assert.Equal(["1664", "1685", "3839", "5923", "4182"], tags[entries["620"]]);
        Assert.Equal(["1663", "19", "3942", "3839"], tags[entries["220"]]);
        Assert.False(tags.ContainsKey(entries["400"]));
        Assert.False(tags.ContainsKey(entries["3999990"]));
        Assert.All(await db.StoreEntries.AsNoTracking().ToListAsync(), entry => Assert.Equal(clock.Now, entry.TagsAskedAt));

        // The names, in both languages, once for the instance.
        var names = await db.StoreTagNames.AsNoTracking().ToDictionaryAsync(name => (name.TagId, name.Language), name => name.Name);
        Assert.Equal(14, names.Count);
        Assert.Equal(("Puzzle", "Casse-tête"), (names[("1664", "en")], names[("1664", "fr")]));
        Assert.All(await db.StoreTagNames.AsNoTracking().ToListAsync(), name => Assert.Equal("steam", name.Source));

        // The answers, untouched and the instance's, not an account's.
        var snapshots = await db.RawSnapshots.AsNoTracking().OrderBy(s => s.Kind).ToListAsync();
        Assert.Equal(["store-tags", "tag-names-en", "tag-names-fr"], snapshots.Select(s => s.Kind));
        Assert.All(snapshots, snapshot => Assert.Equal(("steam", (Guid?)null), (snapshot.Source, snapshot.LinkedAccountId)));
        Assert.Equal(FakeSteam.Recorded("store-tags.json"), snapshots[0].Payload);

        Assert.Contains("store tags: store steam: the store gave the tags of 4 games", app.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_library_carries_each_games_tags_and_their_names_are_a_route_of_their_own()
    {
        var steam = Store();
        await using var app = await steam.StartAppAsync();
        using var browser = app.Browser();
        await LinkAsync(app, browser);
        await TurnAsync<SyncScheduler>(app);

        // Before the store was asked: no tag, and no name.
        Assert.All(await LibraryAsync(browser), game => Assert.Empty(game.Value.GetProperty("store_tags").EnumerateArray()));
        Assert.Empty((await browser.GetAsync("/api/v1/store-tags").JsonAsync()).GetProperty("tags").EnumerateArray());

        Assert.Equal(1, await TagsAsync(app));

        // A game's tags as keys, the heaviest first; one the store gave no name of still among them.
        var library = await LibraryAsync(browser);
        Assert.Equal(["steam:1664", "steam:1685", "steam:3839", "steam:5923", "steam:4182"], Words(library["Portal 2"].GetProperty("store_tags")));
        Assert.Equal(["steam:1663", "steam:19", "steam:3942", "steam:3839"], Words(library["Half-Life 2"].GetProperty("store_tags")));
        Assert.Empty(library["Portal"].GetProperty("store_tags").EnumerateArray());

        // Every tag named, with its name in each language.
        var named = (await browser.GetAsync("/api/v1/store-tags").JsonAsync()).GetProperty("tags").EnumerateArray()
            .ToDictionary(tag => tag.GetProperty("key").GetString()!, tag => tag.GetProperty("names"));
        Assert.Equal(7, named.Count);
        Assert.Equal("Casse-tête", named["steam:1664"].GetProperty("fr").GetString());
        Assert.Equal("Puzzle", named["steam:1664"].GetProperty("en").GetString());
        Assert.False(named.ContainsKey("steam:5923"));

        // Only for someone signed in.
        using var stranger = app.Browser();
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await stranger.GetAsync("/api/v1/store-tags").RefusalAsync());
    }

    [Fact]
    public async Task A_games_tags_are_asked_again_after_thirty_days_and_replace_those_it_had()
    {
        var (steam, clock) = (Store(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        var entries = await AddGamesAsync(app, AppIds);
        Assert.Equal(1, await TagsAsync(app));

        // Nothing is due until then.
        clock.Advance(TimeSpan.FromDays(30) - TimeSpan.FromMinutes(1));
        Assert.Equal(0, await TagsAsync(app));
        Assert.Equal(3, steam.StoreRequests.Count);

        // A month on, players tagged Portal 2 otherwise: its tags are those of the new answer alone.
        clock.Advance(TimeSpan.FromMinutes(2));
        steam.StoreAnswerFor = (_, address) => Answer(address, """
            {"response":{"store_items":[{"id":620,"success":1,"tags":[{"tagid":1685,"weight":900},{"tagid":1664,"weight":800}]},
              {"id":220,"success":1,"tags":[]},{"id":400,"success":1},{"id":3999990,"success":15}]}}
            """);
        Assert.Equal(1, await TagsAsync(app));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var tags = await db.StoreEntryTags.AsNoTracking().OrderBy(tag => tag.Rank).ToListAsync();
        Assert.Equal([(entries["620"], "1685", 0), (entries["620"], "1664", 1)], tags.Select(tag => (tag.StoreEntryId, tag.TagId, tag.Rank)));
    }

    [Fact]
    public async Task A_store_that_cannot_be_read_keeps_nothing_is_said_once_and_asked_again_an_hour_later()
    {
        var (steam, clock) = (Store(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        await AddGamesAsync(app, AppIds);
        steam.StoreAnswerFor = (_, address) => address.AbsolutePath.Contains("GetItems", StringComparison.Ordinal)
            ? (HttpStatusCode.ServiceUnavailable, "")
            : Answer(address, null);

        Assert.Equal(1, await TagsAsync(app));
        Assert.Equal(0, await TagsAsync(app));

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            Assert.Empty(await db.StoreEntryTags.AsNoTracking().ToListAsync());
            Assert.Empty(await db.StoreTagNames.AsNoTracking().ToListAsync());
            Assert.All(await db.StoreEntries.AsNoTracking().ToListAsync(), entry => Assert.Null(entry.TagsAskedAt));
        }

        Assert.Equal(1, app.Logs.Count("store tags: store steam"));

        // An hour on, the store answers again.
        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));
        steam.StoreAnswerFor = (_, address) => Answer(address, null);
        Assert.Equal(1, await TagsAsync(app));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            Assert.Equal(9, await db.StoreEntryTags.CountAsync());
        }
    }

    [Fact]
    public async Task An_answer_without_the_list_of_games_is_not_a_batch_without_tags()
    {
        var (steam, clock) = (Store(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        await AddGamesAsync(app, AppIds);
        steam.StoreAnswerFor = (_, address) => Answer(address, """{"response":{}}""");

        Assert.Equal(1, await TagsAsync(app));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.All(await db.StoreEntries.AsNoTracking().ToListAsync(), entry => Assert.Null(entry.TagsAskedAt));
        Assert.Contains("has no store_items", app.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_game_the_answer_leaves_out_keeps_its_tags_and_does_not_hold_back_the_others()
    {
        var (steam, clock) = (Store(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        var entries = await AddGamesAsync(app, AppIds);
        Assert.Equal(1, await TagsAsync(app));

        // A month on, the answer names Half-Life 2 alone: Portal 2 keeps its tags, and every game asked
        // about is marked as asked.
        clock.Advance(TimeSpan.FromDays(31));
        steam.StoreAnswerFor = (_, address) => Answer(address, """
            {"response":{"store_items":[{"id":220,"success":1,"tags":[{"tagid":19,"weight":10}]}]}}
            """);
        Assert.Equal(1, await TagsAsync(app));

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            var tags = (await db.StoreEntryTags.AsNoTracking().ToListAsync())
                .GroupBy(tag => tag.StoreEntryId)
                .ToDictionary(game => game.Key, game => game.OrderBy(tag => tag.Rank).Select(tag => tag.TagId).ToArray());
            Assert.Equal(["1664", "1685", "3839", "5923", "4182"], tags[entries["620"]]);
            Assert.Equal(["19"], tags[entries["220"]]);
            Assert.All(await db.StoreEntries.AsNoTracking().ToListAsync(), entry => Assert.Equal(clock.Now, entry.TagsAskedAt));
        }

        // Games the answers never name do not come back first at each turn: a new one is asked about.
        var added = await AddGamesAsync(app, ["70"]);
        clock.Advance(TimeSpan.FromMinutes(5));
        steam.StoreAnswerFor = (_, address) => Answer(address, """{"response":{"store_items":[]}}""");
        Assert.Equal(1, await TagsAsync(app));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, await TagsAsync(app));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            Assert.Equal(clock.Now - TimeSpan.FromMinutes(5), (await db.StoreEntries.AsNoTracking().SingleAsync(e => e.Id == added["70"])).TagsAskedAt);
        }
    }

    [Fact]
    public async Task The_names_are_asked_again_a_day_later_not_at_each_turn()
    {
        var (steam, clock) = (Store(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        await AddGamesAsync(app, AppIds);
        Assert.Equal(1, await TagsAsync(app));
        int NameQuestions() => steam.StoreRequests.Count(request => request.Address.AbsolutePath.Contains("GetTagList", StringComparison.Ordinal));
        Assert.Equal(2, NameQuestions());

        // A game bought since: its tags alone are asked, with the names the catalogue has.
        await AddGamesAsync(app, ["70"]);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, await TagsAsync(app));
        Assert.Equal(2, NameQuestions());

        // A day on, the names are asked again with the next game's tags, and replace those kept.
        await AddGamesAsync(app, ["80"]);
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, await TagsAsync(app));
        Assert.Equal(4, NameQuestions());

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.All(await db.StoreTagNames.AsNoTracking().ToListAsync(), name => Assert.Equal(clock.Now, name.ReadAt));
        Assert.Equal(4, await db.RawSnapshots.CountAsync(s => s.Kind.StartsWith("tag-names")));
    }

    [Fact]
    public async Task More_games_than_one_question_takes_are_asked_about_fifty_at_a_time()
    {
        var (steam, clock) = (Store(), new TestClock());
        await using var app = await StartAsync(steam, clock);
        await AddGamesAsync(app, Enumerable.Range(1, 60).Select(n => (n * 10).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        // Every game asked about, with one tag.
        steam.StoreAnswerFor = (_, address) => Answer(
            address,
            "{\"response\":{\"store_items\":["
                + string.Join(',', AppIdsOf(address).Select(id => $"{{\"id\":{id},\"success\":1,\"tags\":[{{\"tagid\":19,\"weight\":10}}]}}"))
                + "]}}");

        Assert.Equal(1, await TagsAsync(app));

        var questions = steam.StoreRequests.Where(request => request.Address.AbsolutePath.Contains("GetItems", StringComparison.Ordinal)).ToList();
        Assert.Equal([50, 10], questions.Select(request => Uri.UnescapeDataString(request.Address.Query).Split("appid").Length - 1));

        // Each name once, however many questions the turn asked.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Equal(2, steam.StoreRequests.Count(request => request.Address.AbsolutePath.Contains("GetTagList", StringComparison.Ordinal)));
        Assert.Equal(2, await db.RawSnapshots.CountAsync(s => s.Kind.StartsWith("tag-names")));
    }

    /// <summary>Steam's store answering the questions of the tags as recorded.</summary>
    private static FakeSteam Store() => new() { StoreAnswerFor = (_, address) => Answer(address, null) };

    /// <summary>What the store answers a question with: the recorded names for the list of tags, and
    /// <paramref name="items"/>, or the recorded tags, for the games.</summary>
    private static (HttpStatusCode, string) Answer(Uri address, string? items) =>
        address.AbsolutePath.Contains("GetTagList", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, FakeSteam.Recorded(address.Query.Contains("french", StringComparison.Ordinal) ? "tag-names-french.json" : "tag-names-english.json"))
            : (HttpStatusCode.OK, items ?? FakeSteam.Recorded("store-tags.json"));

    /// <summary>The app ids a question about the games names.</summary>
    private static IEnumerable<string> AppIdsOf(Uri address) =>
        System.Text.RegularExpressions.Regex.Matches(Uri.UnescapeDataString(address.Query), "\"appid\":([0-9]+)").Select(match => match.Groups[1].Value);

    private static Task<TestApp> StartAsync(FakeSteam steam, TestClock clock) =>
        steam.StartAppAsync(services: services => services.AddSingleton<TimeProvider>(clock));

    /// <summary>Steam games in the catalogue, as a sync would have added them: a record and a store entry
    /// each. Returns the store entries by app id.</summary>
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

    private static Task<int> TagsAsync(TestApp app) => TurnAsync<StoreTagScheduler>(app);

    private static async Task<Dictionary<string, JsonElement>> LibraryAsync(HttpClient browser) =>
        (await browser.GetAsync("/api/v1/library?limit=500").JsonAsync()).GetProperty("games").EnumerateArray()
            .ToDictionary(game => game.GetProperty("name").GetString() ?? "");

    private static string[] Words(JsonElement words) => [.. words.EnumerateArray().Select(word => word.GetString() ?? "")];
}
