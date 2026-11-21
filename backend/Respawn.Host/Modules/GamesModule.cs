using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Respawn.Games;
using Respawn.Games.Api;
using Respawn.Games.Infrastructure;
using Respawn.Games.Ports;
using Respawn.Games.Services;
using Respawn.Games.Steam;
using Respawn.Host.Configuration;

namespace Respawn.Host.Modules;

/// <summary>The Games module put together: its core, the adapters behind its ports and its connectors.
/// Its routes are found with the controllers of the app.</summary>
internal static class GamesModule
{
    public static IServiceCollection AddGames(this IServiceCollection services)
    {
        services.AddScoped<ILibraryService, LibraryService>();
        services.AddScoped<ISyncService, SyncService>();
        services.AddScoped<ICredentialService, CredentialService>();
        // The syncs on their way: one for the app, which is one replica.
        services.AddSingleton<SyncGate>();
        services.AddGamesInfrastructure(provider => RespawnSettings.Encryption(provider.GetRequiredService<IConfiguration>()));
        services.AddGamesApi();
        services.AddSteamConnector(provider => RespawnSettings.Steam(provider.GetRequiredService<IConfiguration>()));

        // Giving or taking away an owner's own key, and coming back from Steam: a bucket of its own for
        // each, per client address, at the pace of the sign-in routes. A key is given once in a while,
        // and a link comes back once; a client that tries many is refused.
        services.AddOptions<RateLimiterOptions>().Configure<HttpSettings>((limiter, http) =>
        {
            foreach (var policy in new[] { GamesApi.OwnKeyRateLimit, GamesApi.SteamReturnRateLimit })
            {
                limiter.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
                    AccountsModule.ClientKey(context.Connection.RemoteIpAddress),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = http.AuthPermits, Window = http.AuthWindow }));
            }
        });
        return services;
    }
}
