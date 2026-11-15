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
        // pick its bucket. The account lockout is the second layer.
        services.AddOptions<RateLimiterOptions>().Configure<HttpSettings>((limiter, http) =>
            limiter.AddPolicy(AccountsApi.SignInRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = http.AuthPermits, Window = http.AuthWindow })));

        return services;
    }
}
