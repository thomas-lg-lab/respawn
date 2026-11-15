using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Respawn.Accounts;
using Respawn.Host.Persistence;

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
/// Read when a request first needs them, not at start-up, so a test host can supply its own.
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

    public static HttpSettings Http(IConfiguration configuration)
    {
        var (permits, window) = RateLimit(configuration["RESPAWN_AUTH_RATE_LIMIT"] ?? "10/minute");
        return new HttpSettings
        {
            CookieSecure = Flag(configuration["RESPAWN_COOKIE_SECURE"], true),
            AuthPermits = permits,
            AuthWindow = window,
            TrustedProxies = Proxies(configuration["RESPAWN_TRUSTED_PROXIES"] ?? "127.0.0.1,::1"),
        };
    }

    private static int Number(IConfiguration configuration, string name, int fallback) =>
        configuration[name] is { Length: > 0 } value ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;

    private static bool Flag(string? value, bool fallback) =>
        value is null ? fallback : value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";

    /// <summary>RESPAWN_OIDC_PROVIDERS is a JSON list of {"name", "issuer", "client_id"}.</summary>
    internal static IReadOnlyList<OidcProvider> Providers(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var entries = JsonSerializer.Deserialize<List<ProviderEntry>>(raw, SnakeCase) ?? [];
        return [.. entries.Select(e => new OidcProvider(e.Name, e.Issuer.TrimEnd('/'), e.ClientId))];
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
        return (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), window);
    }

    /// <summary>Comma-separated addresses or networks: 192.0.2.10, 198.51.100.0/24.</summary>
    private static IReadOnlyList<IPNetwork> Proxies(string value) =>
        [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Network)];

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

    private sealed record ProviderEntry(string Name, string Issuer, string ClientId);
}
