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
        // The syncs on their way: one for the app, which is one replica.
        services.AddSingleton<SyncGate>();
        services.AddGamesInfrastructure();
        services.AddSingleton(provider => new SteamLinkOptions { PublicUrl = provider.GetRequiredService<HttpSettings>().PublicUrl });
        services.AddGamesApi();
        services.AddSteamConnector(provider => RespawnSettings.Steam(provider.GetRequiredService<IConfiguration>()));
        return services;
    }
}
