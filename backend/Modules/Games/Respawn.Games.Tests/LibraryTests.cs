using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Games.Ports;
using Respawn.Testing;

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
        await using var app = await TestApp.StartAsync();
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
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        var document = await browser.GetAsync("/api/v1/openapi.json").JsonAsync();
        var responses = document.GetProperty("paths").GetProperty("/api/v1/library").GetProperty("get").GetProperty("responses");

        Assert.True(responses.TryGetProperty("401", out _));
        var answer = responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.EndsWith("/LibraryResponse", answer.GetProperty("$ref").GetString(), StringComparison.Ordinal);
        var schemas = document.GetProperty("components").GetProperty("schemas");
        Assert.Single(schemas.GetProperty("LibraryResponse").GetProperty("properties").EnumerateObject());
        Assert.Equal(6, schemas.GetProperty("LibraryGameResponse").GetProperty("properties").EnumerateObject().Count());
    }

    [Fact]
    public async Task A_reading_becomes_a_snapshot_the_catalogue_and_a_library()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        var owner = await OwnerAsync(browser);
        var account = await LinkAsync(app, owner, "76561190000000001");

        var snapshot = await RecordAsync(app, account.Id, Day(1), Payload, ("620", "Portal 2", 90, LastPlayed), ("400", "Portal", 0, null));

        // The answer, untouched, dated when the source answered, with its owner.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var stored = await db.RawSnapshots.SingleAsync();
        Assert.Equal(Payload, stored.Payload);
        Assert.Equal((snapshot, Store, "owned-games", Day(1)), (stored.Id, stored.Source, stored.Kind, stored.TakenAt));
        Assert.Equal((owner, account.Id), (stored.OwnerId, stored.LinkedAccountId));
        Assert.InRange(stored.RecordedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);

        // One canonical record per new store entry, and each entry points to its own.
        var entries = await db.StoreEntries.OrderBy(e => e.ExternalId).ToListAsync();
        Assert.Equal([("400", "Portal"), ("620", "Portal 2")], entries.Select(e => (e.ExternalId, e.Name!)).ToArray());
        Assert.All(entries, entry => Assert.Equal(Store, entry.Source));
        var games = await db.Games.ToDictionaryAsync(g => g.Id);
        Assert.Equal(2, games.Count);
        Assert.All(entries, entry => Assert.Equal(entry.Name, games[entry.GameId].Name));
        Assert.All(games.Values, game => Assert.True(game.IgdbId is null && game.NameFromStore));

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
        Assert.Equal((Store, "620", 5400L), (second.GetProperty("source").GetString(), second.GetProperty("external_id").GetString(), second.GetProperty("playtime_seconds").GetInt64()));
        Assert.Equal(LastPlayed, second.GetProperty("last_played_at").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, library[0].GetProperty("last_played_at").ValueKind);
    }

    [Fact]
    public async Task Two_readings_give_two_snapshots_and_a_playtime_difference()
    {
        await using var app = await TestApp.StartAsync();
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
            select new { snapshot.TakenAt, observation.PlaytimeSeconds })
            .ToListAsync();
        Assert.Equal([(Day(1), 5400L), (Day(2), 9000L)], history.Select(h => (h.TakenAt, h.PlaytimeSeconds!.Value)).ToArray());
        Assert.Equal(TimeSpan.FromHours(1), TimeSpan.FromSeconds(history[1].PlaytimeSeconds!.Value - history[0].PlaytimeSeconds!.Value));
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
        await using var app = await TestApp.StartAsync();
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
        Assert.All(snapshots.Values, snapshot => Assert.InRange(snapshot.RecordedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow));

        // Both are history, each at its date.
        Assert.Equal(3, await db.Observations.CountAsync());

        // What is known stays what the most recent answer said; the game was only owned earlier than thought.
        var portal2 = await db.Ownerships.SingleAsync(o => o.SnapshotId == late);
        Assert.Equal((9000L, played, Day(1), answered), (portal2.PlaytimeSeconds!.Value, portal2.LastPlayedAt!.Value, portal2.FirstSeenAt, portal2.AsOf));
        Assert.Equal([("Portal", 1800L), ("Portal 2", 9000L)], await PlaytimesAsync(browser));

        // A reading from the future is a mistake, and would make every later one look old.
        await Assert.ThrowsAsync<ArgumentException>(() => RecordAsync(app, account.Id, DateTimeOffset.UtcNow.AddHours(1), "{}", ("620", "Portal 2", 151, null)));
        Assert.Equal(2, await db.RawSnapshots.CountAsync());
    }

    [Fact]
    public async Task A_poorer_reading_erases_nothing()
    {
        await using var app = await TestApp.StartAsync();
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
    }

    [Fact]
    public async Task A_store_entry_follows_its_store_and_a_game_keeps_a_name_given_otherwise()
    {
        await using var app = await TestApp.StartAsync();
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
            .ExecuteUpdateAsync(game => game.SetProperty(g => g.Name, "Portal Two").SetProperty(g => g.NameFromStore, false));
        // A merge: Half-Life's entry now points to the game that Portal's entry created and named.
        await db.StoreEntries.Where(e => e.ExternalId == "70").ExecuteUpdateAsync(entry => entry.SetProperty(e => e.GameId, entries["400"].GameId));

        await RecordAsync(app, account.Id, Day(3), "{}", ("620", "Portal 2 Remastered", 10, null), ("70", "Half-Life: Source", 10, null));

        var names = await db.StoreEntries.AsNoTracking().ToDictionaryAsync(e => e.ExternalId, e => e.Name);
        Assert.Equal(("Portal 2 Remastered", "Half-Life: Source", "Portal Classic"), (names["620"], names["70"], names["400"]));
        Assert.Equal(["Portal Classic", "Portal Classic", "Portal Two"], await NamesAsync(browser));

        // The entry a merged game took its name from still renames it.
        await RecordAsync(app, account.Id, Day(4), "{}", ("400", "Portal", 10, null));
        Assert.Equal(["Portal", "Portal", "Portal Two"], await NamesAsync(browser));
    }

    [Fact]
    public async Task Two_owners_of_a_game_share_its_record_and_nothing_else()
    {
        await using var app = await TestApp.StartAsync();
        using var first = app.Browser();
        var one = await LinkAsync(app, await OwnerAsync(first), "76561190000000001");
        TestDatabase.Run("INSERT INTO accounts.settings (key, value) VALUES ('registration_open', 'true')");
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
        await using var app = await TestApp.StartAsync();
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
        await using var app = await TestApp.StartAsync();
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
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");

        await RecordAsync(app, account.Id, Day(1), "{}", ("620", "Portal 2", 10, null), ("620", new string('x', 600), 20, null));

        Assert.Equal([(new string('x', 500), 1200L)], await PlaytimesAsync(browser));
    }

    /// <summary>A day of the past, at the hour a daily reading would be taken.</summary>
    private static DateTimeOffset Day(int day) => new(2026, 11, day, 6, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> OwnerAsync(HttpClient browser, string email = "first@example.org") =>
        (await browser.RegisterAsync(email).JsonAsync()).GetProperty("id").GetGuid();

    private static Task<LinkedAccount> LinkAsync(TestApp app, Guid owner, string externalId, string? displayName = null) =>
        UseAsync(app, library => library.LinkAccountAsync(owner, Store, externalId, displayName, default));

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

    /// <summary>A use case, called as a connector or a scheduler will: in a scope of its own.</summary>
    private static async Task<T> UseAsync<T>(TestApp app, Func<ILibraryService, Task<T>> use)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<ILibraryService>());
    }

    private static async Task<JsonElement[]> LibraryAsync(HttpClient browser) =>
        [.. (await browser.GetAsync("/api/v1/library").JsonAsync()).GetProperty("games").EnumerateArray()];

    /// <summary>The names in the order of the library; a game without one reads "(none)".</summary>
    private static async Task<string[]> NamesAsync(HttpClient browser) =>
        [.. (await LibraryAsync(browser)).Select(game => game.GetProperty("name").GetString() ?? "(none)")];

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

        public Task<IReadOnlyList<OwnedGame>> ListAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OwnedGame>>([]);
    }
}
