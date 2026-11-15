using System.Data.Common;
using System.Web;
using Npgsql;

namespace Respawn.Host.Persistence;

/// <summary>Where the connection string comes from: the environment, or a file that may appear, and
/// change, after the app has started.</summary>
public sealed class DatabaseOptions
{
    public string? Url { get; init; }

    public string UrlFile { get; init; } = "/secrets/database-url";

    /// <summary>The connection string as it is now; read again on each call, nothing is cached.</summary>
    public string? Resolve()
    {
        if (!string.IsNullOrWhiteSpace(Url))
        {
            return Url.Trim();
        }

        try
        {
            var text = File.ReadAllText(UrlFile).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

internal static class ConnectionStrings
{
    /// <summary>How long one connection attempt may take; without it an unreachable host holds the
    /// attempt for much longer.</summary>
    public const int ConnectTimeoutSeconds = 5;

    private static readonly string[] UriPrefixes = ["postgresql://", "postgres://"];

    /// <summary>Accepts the plain PostgreSQL URI an operator or a managed database hands out, and
    /// Npgsql's own key=value form.</summary>
    /// <exception cref="FormatException">Not a connection string. The message never holds its values.</exception>
    public static NpgsqlConnectionStringBuilder Parse(string value)
    {
        bool timeoutGiven;
        var kerberosGiven = false;
        NpgsqlConnectionStringBuilder builder;
        try
        {
            if (UriPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                builder = FromUri(value, out timeoutGiven);
            }
            else
            {
                builder = new NpgsqlConnectionStringBuilder(value);
                var given = new DbConnectionStringBuilder { ConnectionString = value };
                timeoutGiven = given.ContainsKey("Timeout");
                kerberosGiven = given.Keys.Cast<string>().Any(key => key.Contains("gss", StringComparison.OrdinalIgnoreCase));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException or OverflowException)
        {
            throw new FormatException("the connection string could not be read", ex);
        }

        if (!timeoutGiven)
        {
            builder.Timeout = ConnectTimeoutSeconds;
        }

        if (!kerberosGiven)
        {
            // Npgsql offers Kerberos encryption by default and looks for a library the image does not
            // carry, which the runtime reports on stderr at every first connection. Sign-in is by password.
            builder.GssEncryptionMode = GssEncryptionMode.Disable;
        }

        return builder;
    }

    private static NpgsqlConnectionStringBuilder FromUri(string value, out bool timeoutGiven)
    {
        var uri = new Uri(value, UriKind.Absolute);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.DnsSafeHost,
            Port = uri.Port > 0 ? uri.Port : 5432,
        };

        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (database.Length > 0)
        {
            builder.Database = database;
        }

        if (uri.UserInfo.Length > 0)
        {
            var parts = uri.UserInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length == 2)
            {
                builder.Password = Uri.UnescapeDataString(parts[1]);
            }
        }

        timeoutGiven = false;
        var query = HttpUtility.ParseQueryString(uri.Query);
        foreach (var key in query.AllKeys)
        {
            var option = query[key] ?? "";
            switch (key?.ToLowerInvariant())
            {
                case "sslmode":
                    builder.SslMode = Enum.Parse<SslMode>(option.Replace("-", "", StringComparison.Ordinal), ignoreCase: true);
                    break;
                case "connect_timeout":
                    builder.Timeout = int.Parse(option, System.Globalization.CultureInfo.InvariantCulture);
                    timeoutGiven = true;
                    break;
                case "application_name":
                    builder.ApplicationName = option;
                    break;
                case "sslrootcert":
                    builder.RootCertificate = option;
                    break;
                default:
                    throw new FormatException($"the connection string has an option this app does not read: {key}");
            }
        }

        return builder;
    }
}
