using System.Data.Common;

namespace Respawn.SharedKernel;

/// <summary>The one PostgreSQL database of the app, which the host connects to. Each module keeps its
/// tables in a schema of its own and never reads another module's.</summary>
public interface IConnectionSource
{
    /// <summary>The data source, connected and migrated on first use. Throws while the database cannot
    /// be used; the host answers 503 for it.</summary>
    DbDataSource DataSource();
}

/// <summary>No connection string yet, the database does not answer, or a schema could not be migrated:
/// what <see cref="IConnectionSource.DataSource"/> throws while the database cannot be used.</summary>
public sealed class DatabaseUnavailableException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>Whether a failure is one, or was caused by one.</summary>
    public static bool Among(Exception? failure)
    {
        for (var cause = failure; cause is not null; cause = cause.InnerException)
        {
            if (cause is DatabaseUnavailableException)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>A module's schema migrations. The host runs every module's when it first connects.</summary>
public interface ISchemaMigrator
{
    /// <summary>Brings the module's schema to its latest migration. Safe to run at every start.</summary>
    void Migrate(DbDataSource dataSource);
}
