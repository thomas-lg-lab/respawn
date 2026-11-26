using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Respawn.Accounts;
using Respawn.Games.Igdb;
using Respawn.Games.Infrastructure.Credentials;
using Respawn.Games.Steam;
using Respawn.Persistence;

namespace Respawn.Host.Configuration;

/// <summary>What the HTTP layer needs to know about where it runs.</summary>
public sealed class HttpSettings
{
    /// <summary>Session cookie sent over HTTPS only; false for plain HTTP on localhost.</summary>
    public bool CookieSecure { get; init; } = true;

    /// <summary>Requests per client address on the sign-in routes, in each window.</summary>
    public int AuthPermits { get; init; } = 10;

    public TimeSpan AuthWindow { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The reverse proxies whose X-Forwarded-For is trusted.</summary>
    public IReadOnlyList<IPNetwork> TrustedProxies { get; init; } = [];

    /// <summary>The address the instance is reached at, scheme and host, when it is not the one the
    /// app itself sees: behind a reverse proxy. Null when the app is reached directly.</summary>
    public Uri? PublicUrl { get; init; }

    public bool IsTrustedProxy(IPAddress peer)
    {
        var address = peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer;
        return TrustedProxies.Any(network => network.Contains(address));
    }
}

/// <summary>
/// Settings, read from the environment (README, "Configuration"). Nothing here is required to start:
/// with no database configured the app still answers /healthz.
///
/// Read once the host is built, so a test host can supply its own, and checked before the app listens
/// (<see cref="SettingsCheck"/>): a value that cannot be read stops the start.
/// </summary>
internal static partial class RespawnSettings
{
    public static AccountOptions Accounts(IConfiguration configuration) => new()
    {
        SessionDays = Number(configuration, "RESPAWN_SESSION_DAYS", 30),
        PasswordMinLength = Number(configuration, "RESPAWN_PASSWORD_MIN_LENGTH", 12),
        LockoutThreshold = Number(configuration, "RESPAWN_LOCKOUT_THRESHOLD", 5),
        LockoutMinutes = Number(configuration, "RESPAWN_LOCKOUT_MINUTES", 15),
        OidcProviders = Providers(configuration["RESPAWN_OIDC_PROVIDERS"]),
    };

    public static DatabaseOptions Database(IConfiguration configuration) => new()
    {
        Url = configuration["RESPAWN_DATABASE_URL"],
        UrlFile = configuration["RESPAWN_DATABASE_URL_FILE"] ?? "/secrets/database-url",
    };

    public static SteamOptions Steam(IConfiguration configuration) => new()
    {
        ApiKey = configuration["RESPAWN_STEAM_API_KEY"],
        ApiKeyFile = configuration["RESPAWN_STEAM_API_KEY_FILE"] ?? "/secrets/steam-api-key",
    };

    public static IgdbOptions Igdb(IConfiguration configuration) => new()
    {
        ClientId = configuration["RESPAWN_IGDB_CLIENT_ID"],
        ClientIdFile = configuration["RESPAWN_IGDB_CLIENT_ID_FILE"] ?? "/secrets/igdb-client-id",
        ClientSecret = configuration["RESPAWN_IGDB_CLIENT_SECRET"],
        ClientSecretFile = configuration["RESPAWN_IGDB_CLIENT_SECRET_FILE"] ?? "/secrets/igdb-client-secret",
    };

    /// <summary>The data directory, where the covers are kept; null when none is given, and the covers
    /// are then passed on from their source at each request. No default: an instance keeps images
    /// only where it was told to, never in a place lost at each restart.</summary>
    /// <exception cref="FormatException">Not an absolute path.</exception>
    public static string? DataDirectory(IConfiguration configuration) =>
        configuration["RESPAWN_DATA_DIR"] is not { Length: > 0 } directory
            ? null
            : Path.IsPathFullyQualified(directory)
                ? directory
                : throw new FormatException("RESPAWN_DATA_DIR must be an absolute path");

    public static EncryptionOptions Encryption(IConfiguration configuration) => new()
    {
        Key = configuration["RESPAWN_ENCRYPTION_KEY"],
        KeyFile = configuration["RESPAWN_ENCRYPTION_KEY_FILE"] ?? "/secrets/encryption-key",
    };

    public static HttpSettings Http(IConfiguration configuration)
    {
        var (permits, window) = RateLimit(configuration["RESPAWN_AUTH_RATE_LIMIT"] ?? "10/minute");
        return new HttpSettings
        {
            CookieSecure = Flag(configuration, "RESPAWN_COOKIE_SECURE", true),
            AuthPermits = permits,
            AuthWindow = window,
            TrustedProxies = Proxies(configuration["RESPAWN_TRUSTED_PROXIES"] ?? "127.0.0.1,::1"),
            PublicUrl = Address(configuration["RESPAWN_PUBLIC_URL"]),
        };
    }

    /// <summary>RESPAWN_PUBLIC_URL: a scheme and a host, with a port when it is not the scheme's own,
    /// and nothing after: https://respawn.example.org.</summary>
    internal static Uri? Address(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var address)
            && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
            && address.AbsolutePath == "/"
            && address.Query.Length == 0
            && address.Fragment.Length == 0
            && address.UserInfo.Length == 0
                ? address
                : throw new FormatException("RESPAWN_PUBLIC_URL must be a scheme and a host, with nothing after: https://respawn.example.org");
    }

    /// <summary>A whole number of at least one: none of these settings means anything at zero.</summary>
    private static int Number(IConfiguration configuration, string name, int fallback)
    {
        if (configuration[name] is not { Length: > 0 } value)
        {
            return fallback;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1
            ? number
            : throw new FormatException($"{name} must be a whole number, 1 or more");
    }

    /// <summary>On or off, said plainly. Anything else is refused: a mistyped "true" must not quietly
    /// turn a protection off.</summary>
    private static bool Flag(IConfiguration configuration, string name, bool fallback)
    {
        if (configuration[name] is not { } value || string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => throw new FormatException($"{name} must be true or false"),
        };
    }

    /// <summary>RESPAWN_OIDC_PROVIDERS is a JSON list of {"name", "issuer", "client_id"}, an optional "label",
    /// what the sign-in page shows (the name when there is none), and an optional "create_accounts": true when
    /// someone the provider proves gets a member account even while registration is closed. The name is a word of the API's
    /// addresses, once per list.</summary>
    internal static IReadOnlyList<OidcProvider> Providers(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        List<ProviderEntry?> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<ProviderEntry?>>(raw, SnakeCase) ?? [];
        }
        catch (JsonException)
        {
            throw new FormatException(ProvidersShape);
        }

        var providers = entries.Select(entry =>
        {
            if (entry is not { Name.Length: > 0, Issuer.Length: > 0, ClientId.Length: > 0 }
                || !ProviderName().IsMatch(entry.Name)
                || !Uri.TryCreate(entry.Issuer, UriKind.Absolute, out var issuer)
                || issuer.Scheme != Uri.UriSchemeHttps
                || (entry.Label is { } label && (label.Trim().Length is 0 or > 40 || label.Any(char.IsControl))))
            {
                throw new FormatException(ProvidersShape);
            }

            return new OidcProvider(
                entry.Name, entry.Label?.Trim() ?? entry.Name, entry.Issuer.TrimEnd('/'), entry.ClientId, entry.CreateAccounts ?? false);
        }).ToList();
        if (providers.DistinctBy(provider => provider.Name).Count() != providers.Count)
        {
            throw new FormatException(ProvidersShape);
        }

        return providers;
    }

    /// <summary>"10/minute": a count, then second, minute or hour.</summary>
    private static (int Permits, TimeSpan Window) RateLimit(string value)
    {
        var match = RateLimitShape().Match(value.Trim());
        if (!match.Success)
        {
            throw new FormatException("RESPAWN_AUTH_RATE_LIMIT must look like 10/minute (second, minute or hour)");
        }

        var window = match.Groups[2].Value.ToLowerInvariant() switch
        {
            "second" => TimeSpan.FromSeconds(1),
            "minute" => TimeSpan.FromMinutes(1),
            _ => TimeSpan.FromHours(1),
        };
        return int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var permits) && permits >= 1
            ? (permits, window)
            : throw new FormatException("RESPAWN_AUTH_RATE_LIMIT must allow at least one request");
    }

    /// <summary>Comma-separated addresses or networks: 192.0.2.10, 198.51.100.0/24.</summary>
    private static IReadOnlyList<IPNetwork> Proxies(string value)
    {
        try
        {
            return [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Network)];
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new FormatException(
                "RESPAWN_TRUSTED_PROXIES must be comma-separated addresses or networks: 192.0.2.10, 198.51.100.0/24", ex);
        }
    }

    private static IPNetwork Network(string entry)
    {
        if (entry.Contains('/', StringComparison.Ordinal))
        {
            return IPNetwork.Parse(entry);
        }

        var address = IPAddress.Parse(entry);
        return new IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
    }

    private static readonly JsonSerializerOptions SnakeCase = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [GeneratedRegex(@"^(\d+)\s*/\s*(second|minute|hour)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RateLimitShape();

    private const string ProvidersShape =
        "RESPAWN_OIDC_PROVIDERS must be a JSON list of {\"name\", \"issuer\", \"client_id\"} with an optional \"label\" of 40 "
        + "characters at most: each name once, in lower case letters, digits and dashes (32 at most), each issuer an https URL";

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]{0,30}[a-z0-9])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderName();

    private sealed record ProviderEntry(string? Name, string? Label, string? Issuer, string? ClientId, bool? CreateAccounts);
}
