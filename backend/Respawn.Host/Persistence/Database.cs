using System.Data.Common;
using Npgsql;
using Respawn.SharedKernel;

namespace Respawn.Host.Persistence;

/// <summary>No connection string yet, the database does not answer, or a schema could not be migrated.</summary>
public sealed class DatabaseUnavailableException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>Whether an exception, or one it wraps, says the database cannot be reached right now
    /// (as opposed to a query it refused).</summary>
    public static bool Explains(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DatabaseUnavailableException or NpgsqlException { IsTransient: true }
                || (current is NpgsqlException && current is not PostgresException))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The app's one PostgreSQL database. The connection is opened on first use, and every module's schema
/// migrated then.
///
/// The app must start, and /healthz answer, before the database is reachable: nothing here runs at
/// start-up. A failed attempt is retried by a later request, after a pause, so an unreachable database
/// or a faulty migration cannot hold every request for the length of a connection attempt.
///
/// The password may be rotated while the app runs: the connection string is read again for every new
/// connection and its password is used. Another host, database or user is not followed, it needs a
/// restart.
/// </summary>
internal sealed class Database(
    DatabaseOptions options,
    IEnumerable<ISchemaMigrator> migrators,
    TimeProvider clock,
    ILogger<Database> logger) : IConnectionSource, IDisposable
{
    /// <summary>Pause before another attempt after a failure. Requests arriving meanwhile are answered
    /// 503 at once.</summary>
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan MigrationRetry = TimeSpan.FromSeconds(60);

    private readonly Lock _gate = new();
    private volatile NpgsqlDataSource? _dataSource;
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;
    private string? _notFollowed; // the changed connection string already warned about

    DbDataSource IConnectionSource.DataSource() => DataSource();

    /// <exception cref="DatabaseUnavailableException">The database cannot be used yet.</exception>
    public NpgsqlDataSource DataSource()
    {
        if (_dataSource is { } ready)
        {
            return ready;
        }

        lock (_gate)
        {
            if (_dataSource is not null)
            {
                return _dataSource;
            }

            if (clock.GetUtcNow() < _retryAt)
            {
                throw new DatabaseUnavailableException("the last attempt failed a moment ago");
            }

            var text = options.Resolve() ?? throw new DatabaseUnavailableException("no database is configured");
            NpgsqlDataSource? candidate = null;
            try
            {
                candidate = Build(ConnectionStrings.Parse(text));
                using var connection = candidate.OpenConnection();
            }
            catch (Exception ex) when (ex is NpgsqlException or FormatException or InvalidOperationException or IOException)
            {
                candidate?.Dispose();
                _retryAt = clock.GetUtcNow() + Retry;
                // The driver's message names the host and the reason, never the password.
                logger.LogWarning("database unreachable: {Reason}", FirstLine(ex));
                throw new DatabaseUnavailableException("the database does not answer", ex);
            }

            try
            {
                foreach (var migrator in migrators)
                {
                    migrator.Migrate(candidate);
                }
            }
            catch (Exception ex)
            {
                candidate.Dispose();
                _retryAt = clock.GetUtcNow() + MigrationRetry;
                logger.LogError(ex, "schema migration failed, next attempt in {Seconds} s", MigrationRetry.TotalSeconds);
                throw new DatabaseUnavailableException("the schema migration failed", ex);
            }

            _dataSource = candidate;
            logger.LogInformation("database ready, schemas up to date");
            return candidate;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _dataSource?.Dispose();
            _dataSource = null;
            _retryAt = DateTimeOffset.MinValue;
        }
    }

    private NpgsqlDataSource Build(NpgsqlConnectionStringBuilder target)
    {
        var password = target.Password;
        if (password is null)
        {
            return new NpgsqlDataSourceBuilder(target.ConnectionString).Build();
        }

        // Npgsql asks for the password each time it opens a connection: the one place a rotation can
        // be followed. The data source itself never holds it.
        var withoutPassword = WithoutPassword(target);
        var builder = new NpgsqlDataSourceBuilder(withoutPassword);
        builder.UsePasswordProvider(
            _ => CurrentPassword(withoutPassword, password),
            (_, _) => ValueTask.FromResult(CurrentPassword(withoutPassword, password)));
        return builder.Build();
    }

    /// <summary>The password as it is now, when the connection string still names the same server,
    /// database and user. Nothing else is followed: a connection string naming another host or database
    /// would send some requests there, to schemas that were never migrated.</summary>
    private string CurrentPassword(string target, string password)
    {
        try
        {
            var text = options.Resolve();
            if (text is null)
            {
                return password;
            }

            var current = ConnectionStrings.Parse(text);
            if (current.Password is null)
            {
                return password;
            }

            if (WithoutPassword(current) == target)
            {
                return current.Password;
            }

            if (text != _notFollowed)
            {
                _notFollowed = text;
                logger.LogWarning(
                    "the connection string now names another host, database, user or option: only a new "
                    + "password is followed while the app runs, restart it to use the new one");
            }
        }
        catch (FormatException)
        {
            // Not a connection string: keep connecting with the one the data source was built with.
        }

        return password;
    }

    private static string WithoutPassword(NpgsqlConnectionStringBuilder builder)
    {
        var copy = new NpgsqlConnectionStringBuilder(builder.ConnectionString);
        copy.Remove("Password");
        return copy.ConnectionString;
    }

    private static string FirstLine(Exception exception)
    {
        var line = exception.Message.Split('\n', 2)[0].Trim();
        return line.Length > 0 ? line : exception.GetType().Name;
    }
}
