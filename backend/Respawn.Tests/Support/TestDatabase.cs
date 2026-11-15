using Npgsql;
using Respawn.Host.Persistence;

// The tests share one database and empty it: they run one after the other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Respawn.Tests.Support;

/// <summary>
/// The tests run against a real PostgreSQL, in a database of their own.
///
/// RESPAWN_TEST_DATABASE_URL names it; by default it is `respawn_test` on the compose server, beside
/// the database the compose app uses and never that one. It is created when missing. The tests empty
/// its tables, so a database whose name does not end in `_test` is refused.
/// </summary>
internal static class TestDatabase
{
    public static readonly string Url =
        Environment.GetEnvironmentVariable("RESPAWN_TEST_DATABASE_URL")
        ?? "postgresql://respawn:respawn@localhost:5432/respawn_test";

    private static readonly Lazy<string> Created = new(() =>
    {
        Create(Url);
        return Url;
    });

    public static string Name => ConnectionStrings.Parse(Url).Database!;

    public static void EnsureCreated() => _ = Created.Value;

    /// <summary>The same server, as a URI naming another database and, when given, another account.</summary>
    public static string UrlFor(string database, string? user = null, string? password = null)
    {
        var target = ConnectionStrings.Parse(Url);
        var credentials = $"{Uri.EscapeDataString(user ?? target.Username!)}:{Uri.EscapeDataString(password ?? target.Password!)}";
        return $"postgresql://{credentials}@{target.Host}:{target.Port}/{database}";
    }

    public static void Create(string url, string? owner = null)
    {
        var name = ConnectionStrings.Parse(url).Database;
        if (name is null || !name.EndsWith("_test", StringComparison.Ordinal) || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            throw new InvalidOperationException($"the tests empty their database: '{name}' is refused, its name must end in _test");
        }

        using var admin = OpenAdmin();
        using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", admin);
        exists.Parameters.AddWithValue("name", name);
        if (exists.ExecuteScalar() is null)
        {
            Execute(admin, $"CREATE DATABASE \"{name}\"" + (owner is null ? "" : $" OWNER \"{owner}\""));
        }
    }

    /// <summary>Runs statements on the maintenance database: where databases and roles are created.</summary>
    public static void Admin(params string[] statements)
    {
        using var admin = OpenAdmin();
        foreach (var statement in statements)
        {
            Execute(admin, statement);
        }
    }

    public static void Truncate()
    {
        using var connection = Open(Url);
        Execute(connection, "TRUNCATE accounts.users, accounts.sessions, accounts.external_identities, accounts.exchanged_tokens, accounts.settings, accounts.user_claims, accounts.user_tokens CASCADE");
    }

    public static NpgsqlConnection Open(string url)
    {
        var target = ConnectionStrings.Parse(url);
        target.Pooling = false; // nothing of a test stays connected after it
        var connection = new NpgsqlConnection(target.ConnectionString);
        connection.Open();
        return connection;
    }

    private static NpgsqlConnection OpenAdmin() => Open(UrlFor("postgres"));

#pragma warning disable CA2100 // the statements are the tests' own, with names checked above
    private static void Execute(NpgsqlConnection connection, string statement)
    {
        using var command = new NpgsqlCommand(statement, connection);
        command.ExecuteNonQuery();
    }
#pragma warning restore CA2100
}
