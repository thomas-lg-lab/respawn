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

    private static readonly DateTimeOffset LastPlayed = new(2026, 11, 1, 20, 30, 0, TimeSpan.Zero);

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
    public async Task A_reading_becomes_a_snapshot_the_catalogue_and_a_library()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        var owner = await OwnerAsync(browser);
        var account = await LinkAsync(app, owner, "76561190000000001");

        var snapshot = await RecordAsync(app, account.Id, Payload, ("620", "Portal 2", 90, LastPlayed), ("400", "Portal", 0, null));

        // The answer, untouched, dated, with its owner.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var stored = await db.RawSnapshots.SingleAsync();
        Assert.Equal(Payload, stored.Payload);
        Assert.Equal((snapshot, Store, "owned-games"), (stored.Id, stored.Source, stored.Kind));
        Assert.Equal((owner, account.Id), (stored.OwnerId, stored.LinkedAccountId));
        Assert.InRange(stored.TakenAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);

        // One canonical record per new store entry, and each entry points to its own.
        var entries = await db.StoreEntries.OrderBy(e => e.ExternalId).ToListAsync();
        Assert.Equal([("400", "Portal"), ("620", "Portal 2")], entries.Select(e => (e.ExternalId, e.Name)).ToArray());
        Assert.All(entries, entry => Assert.Equal(Store, entry.Source));
        var games = await db.Games.ToDictionaryAsync(g => g.Id);
        Assert.Equal(2, games.Count);
        Assert.All(entries, entry => Assert.Equal(entry.Name, games[entry.GameId].Name));
        Assert.All(games.Values, game => Assert.Null(game.IgdbId));

        // Ownership and playtime, per store entry, traced to the snapshot.
        var ownerships = await db.Ownerships.ToDictionaryAsync(o => o.StoreEntryId);
        Assert.Equal(2, ownerships.Count);
        var played = ownerships[entries[1].Id];
        Assert.Equal((account.Id, 90 * 60L, snapshot), (played.LinkedAccountId, played.PlaytimeSeconds, played.SnapshotId));
        Assert.Equal(LastPlayed, played.LastPlayedAt);
        Assert.Equal(0L, ownerships[entries[0].Id].PlaytimeSeconds);
        Assert.Null(ownerships[entries[0].Id].LastPlayedAt);

        // And the route, sorted by name.
        var library = await LibraryAsync(browser);
        Assert.Equal(["Portal", "Portal 2"], library.Select(g => g.GetProperty("name").GetString()!).ToArray());
        var second = library[1];
        Assert.Equal(games.Values.Single(g => g.Name == "Portal 2").Id, second.GetProperty("game_id").GetGuid());
        Assert.Equal((Store, "620", 5400L), (second.GetProperty("source").GetString(), second.GetProperty("external_id").GetString(), second.GetProperty("playtime_seconds").GetInt64()));
        Assert.Equal(LastPlayed, second.GetProperty("last_played_at").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, library[0].GetProperty("last_played_at").ValueKind);
    }

    [Fact]
    public async Task A_second_reading_adds_a_snapshot_and_moves_the_playtime()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        var account = await LinkAsync(app, await OwnerAsync(browser), "76561190000000001");
        var first = await RecordAsync(app, account.Id, "{\"day\":1}", ("620", "Portal 2", 90, null), ("400", "Portal", 30, null));

        // The next day: one game played, one bought, and one the store no longer lists.
        var second = await RecordAsync(app, account.Id, "{\"day\":2}", ("620", "Portal 2", 150, LastPlayed), ("70", "Half-Life", 0, null));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Equal(["{\"day\":1}", "{\"day\":2}"], await db.RawSnapshots.OrderBy(s => s.TakenAt).Select(s => s.Payload).ToArrayAsync());
        Assert.Equal(3, await db.StoreEntries.CountAsync());
        Assert.Equal(3, await db.Games.CountAsync());

        var ownerships = await (
            from ownership in db.Ownerships
            join entry in db.StoreEntries on ownership.StoreEntryId equals entry.Id
            select new { entry.ExternalId, ownership.PlaytimeSeconds, ownership.SnapshotId, ownership.FirstSeenAt })
            .ToDictionaryAsync(o => o.ExternalId);
        Assert.Equal((150 * 60L, second), (ownerships["620"].PlaytimeSeconds, ownerships["620"].SnapshotId));
        Assert.Equal((0L, second), (ownerships["70"].PlaytimeSeconds, ownerships["70"].SnapshotId));
        // Not listed any more: left as the first reading said.
        Assert.Equal((30 * 60L, first), (ownerships["400"].PlaytimeSeconds, ownerships["400"].SnapshotId));
        Assert.True(ownerships["620"].FirstSeenAt < ownerships["70"].FirstSeenAt);

        Assert.Equal(
            [("Half-Life", 0L), ("Portal", 1800L), ("Portal 2", 9000L)],
            (await LibraryAsync(browser)).Select(g => (g.GetProperty("name").GetString()!, g.GetProperty("playtime_seconds").GetInt64())).ToArray());
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

        await RecordAsync(app, one.Id, "{}", ("620", "Portal 2", 90, null), ("400", "Portal", 10, null));
        await RecordAsync(app, other.Id, "{}", ("620", "Portal 2", 600, null));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Equal(2, await db.StoreEntries.CountAsync());
        Assert.Equal(2, await db.Games.CountAsync());
        Assert.Equal(3, await db.Ownerships.CountAsync());

        var mine = await LibraryAsync(first);
        var theirs = await LibraryAsync(second);
        Assert.Equal([36000L], theirs.Select(g => g.GetProperty("playtime_seconds").GetInt64()).ToArray());
        Assert.Equal([600L, 5400L], mine.Select(g => g.GetProperty("playtime_seconds").GetInt64()).ToArray());
        Assert.Equal(mine[1].GetProperty("game_id").GetGuid(), theirs[0].GetProperty("game_id").GetGuid());
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

        var unknown = await Assert.ThrowsAsync<GamesException>(() => RecordAsync(app, Guid.CreateVersion7(), "{}", ("620", "Portal 2", 1, null)));
        Assert.Equal(GamesError.UnknownLinkedAccount, unknown.Error);
        await Assert.ThrowsAsync<ArgumentException>(() => RecordAsync(app, account.Id, "{}", ("620", " ", 1, null)));
        await Assert.ThrowsAsync<ArgumentException>(() => RecordAsync(app, account.Id, "{}", ("620", "Portal 2", -1, null)));
        await Assert.ThrowsAsync<ArgumentException>(() => UseAsync(app, library => library.LinkAccountAsync(account.OwnerId, "Steam Store", "1", null, default)));

        // The snapshot and the catalogue are one transaction with the library: a failure half-way keeps none.
        await using var broken = TestApp.Create(services: services => services.AddScoped<ILibraryStore, FailingLibrary>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => RecordAsync(broken, account.Id, "{}", ("620", "Portal 2", 1, null)));

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

        await RecordAsync(app, account.Id, "{}", ("620", "Portal 2", 10, null), ("620", new string('x', 600), 20, null));

        var game = Assert.Single(await LibraryAsync(browser));
        Assert.Equal((new string('x', 500), 1200L), (game.GetProperty("name").GetString(), game.GetProperty("playtime_seconds").GetInt64()));
    }

    private static async Task<Guid> OwnerAsync(HttpClient browser, string email = "first@example.org") =>
        (await browser.RegisterAsync(email).JsonAsync()).GetProperty("id").GetGuid();

    private static Task<LinkedAccount> LinkAsync(TestApp app, Guid owner, string externalId, string? displayName = null) =>
        UseAsync(app, library => library.LinkAccountAsync(owner, Store, externalId, displayName, default));

    private static Task<Guid> RecordAsync(
        TestApp app, Guid account, string payload, params (string Id, string Name, int Minutes, DateTimeOffset? LastPlayed)[] items)
    {
        var reading = new LibraryReading(
            "owned-games",
            payload,
            [.. items.Select(item => new OwnedItem(item.Id, item.Name, TimeSpan.FromMinutes(item.Minutes), item.LastPlayed))]);
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

    /// <summary>The last step of a recording, failing.</summary>
    private sealed class FailingLibrary : ILibraryStore
    {
        public Task RecordAsync(
            Guid linkedAccountId, Guid snapshotId, IReadOnlyCollection<Ownership> owned, DateTimeOffset at, CancellationToken ct) =>
            throw new InvalidOperationException("the library cannot be written");

        public Task<IReadOnlyList<OwnedGame>> ListAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OwnedGame>>([]);
    }
}
