using Npgsql;
using Respawn.Persistence;

namespace Respawn.Testing;

/// <summary>
/// The tests run against a real PostgreSQL, each test project in a database of its own: the projects
/// run side by side, and each empties its tables.
///
/// RESPAWN_TEST_DATABASE_URL names the server and a base name, `respawn_test` on the compose server by
/// default; a project's database takes the project's name in it (`respawn_accounts_test`,
/// `respawn_host_test`), beside the database the compose app uses and never that one. It is created
/// when missing. A base name that does not end in `_test` is refused.
/// </summary>
internal static class TestDatabase
{
    private const string Suffix = "_test";

    private static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("RESPAWN_TEST_DATABASE_URL")
        ?? "postgresql://respawn:respawn@localhost:5432/respawn_test";

    public static readonly string Url = UrlFor(OwnName());

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
        var target = ConnectionStrings.Parse(BaseUrl);
        var credentials = $"{Uri.EscapeDataString(user ?? target.Username!)}:{Uri.EscapeDataString(password ?? target.Password!)}";
        return $"postgresql://{credentials}@{target.Host}:{target.Port}/{database}";
    }

    /// <summary>The base name with the test project's name in it: Respawn.Accounts.Tests gives
    /// respawn_accounts_test, and Respawn.Tests, the host's tests, respawn_host_test.</summary>
    private static string OwnName()
    {
        var name = ConnectionStrings.Parse(BaseUrl).Database ?? "";
        if (!name.EndsWith(Suffix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"the tests empty their database: '{name}' is refused, its name must end in {Suffix}");
        }

        string[] projects =
        [
            .. AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetName().Name ?? "")
                .Where(assembly => assembly.StartsWith("Respawn.", StringComparison.Ordinal) && assembly.EndsWith(".Tests", StringComparison.Ordinal)),
        ];
        if (projects is not [var project])
        {
            throw new InvalidOperationException(
                "the test database is named after the one test project that runs, and these are loaded: "
                + $"{string.Join(", ", projects)}; a test project must not reference another");
        }

        var module = project == "Respawn.Tests"
            ? "host"
            : project["Respawn.".Length..^".Tests".Length].Replace('.', '_').ToLowerInvariant();
        return $"{name[..^Suffix.Length]}_{module}{Suffix}";
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

    /// <summary>Runs a statement on the tests' database: what an administrator's route will do later.</summary>
    public static void Run(string statement)
    {
        using var connection = Open(Url);
        Execute(connection, statement);
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
