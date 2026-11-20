using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Respawn.Accounts.Api;
using Respawn.Accounts.Infrastructure;
using Respawn.Accounts.Ports;
using Respawn.Accounts.Services;
using Respawn.Host.Configuration;

namespace Respawn.Host.Modules;

/// <summary>The Accounts module put together: its core, the adapters behind its ports, its routes, and
/// what the environment says about them. Each module has one such method, and only the host calls it.</summary>
internal static class AccountsModule
{
    public static IServiceCollection AddAccounts(this IServiceCollection services)
    {
        services.AddSingleton(provider => RespawnSettings.Accounts(provider.GetRequiredService<IConfiguration>()));
        services.AddSingleton(provider => new SessionCookieOptions { Secure = provider.GetRequiredService<HttpSettings>().CookieSecure });

        services.AddScoped<IAccountService, AccountService>();
        services.AddAccountsInfrastructure();
        services.AddAccountsApi();

        // One bucket per client address on the sign-in routes. The address is the peer's, or the one a
        // listed reverse proxy reports; the header of an unlisted peer is ignored, so a client cannot
        // pick its bucket. The account lockout is the second layer. The returns from a provider have a
        // bucket of their own, of the same size: wrong passwords never use up the return of a sign-in.
        services.AddOptions<RateLimiterOptions>().Configure<HttpSettings>((limiter, http) =>
        {
            foreach (var policy in new[] { AccountsApi.SignInRateLimit, AccountsApi.ProviderReturnRateLimit })
            {
                limiter.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context.Connection.RemoteIpAddress),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = http.AuthPermits, Window = http.AuthWindow }));
            }
        });

        return services;
    }

    /// <summary>What one client is, for the rate limit: an IPv4 address, or the /64 of an IPv6 one. A
    /// network hands a whole /64 to one subscriber, who would otherwise have a bucket per address.</summary>
    internal static string ClientKey(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
            ? Convert.ToHexStringLower(address.GetAddressBytes().AsSpan(0, 8))
            : address.ToString();
    }
}
