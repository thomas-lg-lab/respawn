using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Testing;

namespace Respawn.Games.Tests;

/// <summary>What the games schema itself refuses, whatever the code above it does.</summary>
public sealed class SchemaTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task A_store_entry_points_to_one_canonical_record()
    {
        await using var app = await TestApp.StartAsync();
        var game = new GameRow { Id = Guid.CreateVersion7(), Name = "Portal 2", IgdbId = 72, CreatedAt = Now };
        await SaveAsync(app, game, Entry(game.Id, "steam", "620"));

        // Another store's entry for the same game points to the same record.
        await SaveAsync(app, Entry(game.Id, "gog", "1207658924"));

        // No record, a store's id twice, an IGDB id twice: refused.
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, await RefusalAsync(app, Entry(Guid.CreateVersion7(), "steam", "400")));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, await RefusalAsync(app, Entry(game.Id, "steam", "620")));
        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            await RefusalAsync(app, new GameRow { Id = Guid.CreateVersion7(), Name = "Portal 2 again", IgdbId = 72, CreatedAt = Now }));

        // Any number of records without an IGDB id or a name, and a record that has entries cannot go.
        await SaveAsync(
            app,
            new GameRow { Id = Guid.CreateVersion7(), Name = "Unmatched", CreatedAt = Now },
            new GameRow { Id = Guid.CreateVersion7(), CreatedAt = Now });
        Assert.Equal(PostgresErrorCodes.RestrictViolation, await RefusalAsync(app, db => db.Games.Where(g => g.Id == game.Id).ExecuteDeleteAsync()));
    }

    [Fact]
    public async Task A_snapshot_is_personal_or_the_instances()
    {
        await using var app = await TestApp.StartAsync();
        var owner = Guid.CreateVersion7();
        var account = new LinkedAccountRow { Id = Guid.CreateVersion7(), OwnerId = owner, Source = "steam", ExternalId = "76561190000000001", LinkedAt = Now };
        await SaveAsync(app, account);

        // The instance's: what a source says of a game, to nobody in particular.
        await SaveAsync(app, Snapshot(null, null));
        await SaveAsync(app, Snapshot(owner, account.Id));

        // An owner without the store account it was read from, or the reverse: refused.
        Assert.Equal(PostgresErrorCodes.CheckViolation, await RefusalAsync(app, Snapshot(owner, null)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, await RefusalAsync(app, Snapshot(null, account.Id)));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, await RefusalAsync(app, Snapshot(owner, Guid.CreateVersion7())));
    }

    [Fact]
    public async Task Ownership_is_per_linked_account_and_store_entry()
    {
        await using var app = await TestApp.StartAsync();
        var game = new GameRow { Id = Guid.CreateVersion7(), Name = "Portal 2", CreatedAt = Now };
        var entry = Entry(game.Id, "steam", "620");
        var account = new LinkedAccountRow { Id = Guid.CreateVersion7(), OwnerId = Guid.CreateVersion7(), Source = "steam", ExternalId = "76561190000000001", LinkedAt = Now };
        var snapshot = Snapshot(account.OwnerId, account.Id);
        await SaveAsync(app, game, entry, account, snapshot);
        OwnershipRow Owned(long seconds) => new()
        {
            LinkedAccountId = account.Id,
            StoreEntryId = entry.Id,
            PlaytimeSeconds = seconds,
            FirstSeenAt = Now,
            AsOf = Now,
            SnapshotId = snapshot.Id,
        };
        await SaveAsync(app, Owned(60));

        // What a snapshot said of a game is said once, and is not a negative playtime either.
        ObservationRow Said(long? seconds) => new() { SnapshotId = snapshot.Id, StoreEntryId = entry.Id, PlaytimeSeconds = seconds };
        await SaveAsync(app, Said(null));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, await RefusalAsync(app, Said(60)));
        Assert.Equal(
            PostgresErrorCodes.CheckViolation,
            await RefusalAsync(app, db => db.Observations.ExecuteUpdateAsync(o => o.SetProperty(x => x.PlaytimeSeconds, -1L))));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, await RefusalAsync(app, Owned(120)));
        Assert.Equal(
            PostgresErrorCodes.CheckViolation,
            await RefusalAsync(app, db => db.Ownerships.ExecuteUpdateAsync(o => o.SetProperty(x => x.PlaytimeSeconds, -1L))));

        // A store account is linked once, and nothing a library stands on can be deleted under it.
        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            await RefusalAsync(app, new LinkedAccountRow { Id = Guid.CreateVersion7(), OwnerId = Guid.CreateVersion7(), Source = "steam", ExternalId = account.ExternalId, LinkedAt = Now }));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, await RefusalAsync(app, db => db.LinkedAccounts.ExecuteDeleteAsync()));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, await RefusalAsync(app, db => db.RawSnapshots.ExecuteDeleteAsync()));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, await RefusalAsync(app, db => db.StoreEntries.ExecuteDeleteAsync()));
    }

    private static StoreEntryRow Entry(Guid game, string source, string externalId) =>
        new() { Id = Guid.CreateVersion7(), GameId = game, Source = source, ExternalId = externalId, Name = "Portal 2", CreatedAt = Now };

    private static RawSnapshotRow Snapshot(Guid? owner, Guid? account) =>
        new() { Id = Guid.CreateVersion7(), Source = "steam", Kind = "owned-games", TakenAt = Now, RecordedAt = Now, OwnerId = owner, LinkedAccountId = account, Payload = "{}" };

    private static async Task SaveAsync(TestApp app, params object[] rows)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static Task<string> RefusalAsync(TestApp app, object row) => RefusalAsync(app, async (GamesDbContext db) =>
    {
        db.Add(row);
        await db.SaveChangesAsync();
    });

    /// <summary>The PostgreSQL error code the database refuses a statement with.</summary>
    private static async Task<string> RefusalAsync(TestApp app, Func<GamesDbContext, Task> statement)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => statement(db));
        var error = refused as PostgresException ?? refused.InnerException as PostgresException;
        Assert.True(error is not null, $"refused by something else than the database: {refused}");
        return error.SqlState;
    }
}
