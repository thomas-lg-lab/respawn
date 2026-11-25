using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Data.Common;
using Respawn.Accounts.Infrastructure.Persistence;
using Respawn.Persistence;
using Respawn.SharedKernel;
using Respawn.Testing;

namespace Respawn.Tests;

/// <summary>The database layer when things go wrong: unreachable, a failed migration, a changed
/// connection string.</summary>
public sealed class DatabaseTests
{
    private const string Role = "respawn_rotation_test";

    [Fact]
    public async Task An_unreachable_database_is_given_up_quickly()
    {
        // An address of the documentation range: nothing answers there.
        await using var app = TestApp.Create(new()
        {
            ["RESPAWN_DATABASE_URL"] = "postgresql://respawn:not-in-the-log@192.0.2.1:5432/respawn_test?connect_timeout=1",
        });
        using var client = app.Browser();

        var watch = Stopwatch.StartNew();
        Assert.Equal(
            (HttpStatusCode.ServiceUnavailable, "database_unavailable"),
            await client.GetAsync("/api/v1/config").RefusalAsync());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), "the connection timeout, not what the network stack would wait");

        // Until the next attempt is due, requests are answered at once instead of each waiting in turn.
        watch.Restart();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await client.GetAsync("/api/v1/config").StatusAsync());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(0.5));
        Assert.Equal(HttpStatusCode.OK, await client.GetAsync("/healthz").StatusAsync());

        Assert.Equal(1, app.Logs.Count("database unreachable"));
        Assert.DoesNotContain("not-in-the-log", app.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_connection_string_is_read_in_both_forms()
    {
        var uri = ConnectionStrings.Parse("postgresql://play%40er:p%3Ass@db.example.org:6432/games?sslmode=verify-full");
        Assert.Equal(("db.example.org", 6432, "games"), (uri.Host, uri.Port, uri.Database));
        Assert.Equal(("play@er", "p:ss"), (uri.Username, uri.Password));
        Assert.Equal(SslMode.VerifyFull, uri.SslMode);

        var keyValue = ConnectionStrings.Parse("Host=db.example.org;Username=player;Password=secret;Database=games");
        Assert.Equal(("db.example.org", 5432, "games"), (keyValue.Host, keyValue.Port, keyValue.Database));

        // A password is not an option: only a Kerberos key keeps Npgsql's default for it.
        Assert.Equal(GssEncryptionMode.Disable, ConnectionStrings.Parse("Host=db.example.org;Password=bigsstuff").GssEncryptionMode);
        Assert.Equal(GssEncryptionMode.Prefer, ConnectionStrings.Parse("Host=db.example.org;GSS Encryption Mode=Prefer").GssEncryptionMode);

        var unknown = Assert.Throws<FormatException>(() => ConnectionStrings.Parse("postgresql://u:secret@h/d?target_session_attrs=any"));
        Assert.DoesNotContain("secret", unknown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_connect_timeout_in_the_connection_string_is_kept()
    {
        Assert.Equal(ConnectionStrings.ConnectTimeoutSeconds, ConnectionStrings.Parse(TestDatabase.Url).Timeout);
        Assert.Equal(2, ConnectionStrings.Parse(TestDatabase.Url + "?connect_timeout=2").Timeout);
        Assert.Equal(ConnectionStrings.ConnectTimeoutSeconds, ConnectionStrings.Parse("Host=db.example.org").Timeout);
        Assert.Equal(7, ConnectionStrings.Parse("Host=db.example.org;Timeout=7").Timeout);
    }

    [Fact]
    public async Task A_failed_migration_is_logged_and_not_retried_at_once()
    {
        TestDatabase.EnsureCreated();
        var migrator = new BrokenMigrator();
        await using var app = TestApp.Create(services: services => services.AddSingleton<ISchemaMigrator>(migrator));
        using var client = app.Browser();

        for (var request = 0; request < 3; request++)
        {
            Assert.Equal(
                (HttpStatusCode.ServiceUnavailable, "database_unavailable"),
                await client.GetAsync("/api/v1/config").RefusalAsync());
        }

        Assert.Equal(HttpStatusCode.OK, await client.GetAsync("/healthz").StatusAsync());
        Assert.Equal(1, migrator.Attempts);
        Assert.Contains("schema migration failed", app.Logs.Text, StringComparison.Ordinal);
        Assert.Contains("the migration says what went wrong", app.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_database_in_the_connection_string_is_not_followed()
    {
        // Only the password is taken on the fly: no request is served from a database that was not migrated.
        await using var app = await TestApp.StartAsync(); // the test database, migrated and empty
        var other = OtherDatabase("respawn_other_test"); // empty: no table at all
        var file = Path.Combine(Directory.CreateTempSubdirectory("respawn-secrets-").FullName, "database-url");
        await File.WriteAllTextAsync(file, TestDatabase.Url);
        var logs = new LogCollector();
        using var loggers = new LoggerFactory([logs]);
        using var database = new Database(
            new DatabaseOptions { UrlFile = file }, [new AccountsSchemaMigrator()], TimeProvider.System, loggers.CreateLogger<Database>());
        var dataSource = database.DataSource();
        await File.WriteAllTextAsync(file, other);

        // Three connections at once: the pool has to open new ones, with the file naming the other database.
        var connections = new[] { dataSource.OpenConnection(), dataSource.OpenConnection(), dataSource.OpenConnection() };
        try
        {
            foreach (var connection in connections)
            {
                Assert.Equal(TestDatabase.Name, Scalar(connection, "SELECT current_database()"));
                Assert.Equal(0L, Scalar(connection, "SELECT count(*) FROM accounts.users"));
            }
        }
        finally
        {
            foreach (var connection in connections)
            {
                connection.Dispose();
            }
        }

        Assert.Equal(1, logs.Count("restart it to use the new one"));
    }

    [Fact]
    public async Task A_rotated_password_is_picked_up_without_a_restart()
    {
        TestDatabase.EnsureCreated();
        TestDatabase.Admin(
            $"DROP DATABASE IF EXISTS \"{Role}\" WITH (FORCE)",
            $"DROP ROLE IF EXISTS \"{Role}\"",
            $"CREATE ROLE \"{Role}\" LOGIN PASSWORD 'before-rotation'");
        OtherDatabase(Role, owner: Role);
        var file = Path.Combine(Directory.CreateTempSubdirectory("respawn-secrets-").FullName, "database-url");
        await File.WriteAllTextAsync(file, TestDatabase.UrlFor(Role, Role, "before-rotation"));

        await using var app = TestApp.Offline(file);
        using var client = app.Browser();
        Assert.Equal(HttpStatusCode.OK, await client.GetAsync("/api/v1/config").StatusAsync());

        // The operator rotates the password: the database first, then the mounted file; connections drop.
        TestDatabase.Admin(
            $"ALTER ROLE \"{Role}\" PASSWORD 'after-rotation'",
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE usename = '{Role}'");
        await File.WriteAllTextAsync(file, TestDatabase.UrlFor(Role, Role, "after-rotation"));

        // The broken connection is replaced, and the new one reads the file again.
        Assert.Equal(HttpStatusCode.OK, await client.GetAsync("/api/v1/config").StatusAsync());
    }

    /// <summary>A second, empty database on the same server; its connection string.</summary>
    private static string OtherDatabase(string name, string? owner = null)
    {
        TestDatabase.Admin($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
        var url = TestDatabase.UrlFor(name);
        TestDatabase.Create(url, owner);
        return url;
    }

    private static object? Scalar(NpgsqlConnection connection, string query)
    {
        using var command = connection.CreateCommand();
        command.CommandText = query;
        return command.ExecuteScalar();
    }

    [Fact]
    public void A_refused_sign_in_to_the_database_is_unavailability()
    {
        // What a new connection gets between a password rotation on the server and its arrival here.
        var refused = new PostgresException("password authentication failed", "FATAL", "FATAL", PostgresErrorCodes.InvalidPassword);
        Assert.True(DatabaseFailures.MeanUnavailable(new InvalidOperationException("wrapped", refused)));

        var query = new PostgresException("syntax error", "ERROR", "ERROR", PostgresErrorCodes.SyntaxError);
        Assert.False(DatabaseFailures.MeanUnavailable(query));
    }

    private sealed class BrokenMigrator : ISchemaMigrator
    {
        public int Attempts { get; private set; }

        public void Migrate(DbDataSource dataSource)
        {
            Attempts++;
            throw new InvalidOperationException("the migration says what went wrong");
        }
    }
}
