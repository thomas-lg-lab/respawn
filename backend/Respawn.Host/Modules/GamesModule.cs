using Respawn.Games.Infrastructure;
using Respawn.Games.Ports;
using Respawn.Games.Services;

namespace Respawn.Host.Modules;

/// <summary>The Games module put together: its core and the adapters behind its ports. Its routes are
/// found with the controllers of the app.</summary>
internal static class GamesModule
{
    public static IServiceCollection AddGames(this IServiceCollection services)
    {
        services.AddScoped<ILibraryService, LibraryService>();
        services.AddGamesInfrastructure();
        return services;
    }
}
