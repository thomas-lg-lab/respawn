using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Ports;

namespace Respawn.Games.Steam;

public static class DependencyInjection
{
    /// <summary>The Steam connector, behind <see cref="ILibrarySource"/>. Nothing is read here: the
    /// key is looked for when a library is asked for. The host says where the key comes from and
    /// registers a <see cref="TimeProvider"/>.</summary>
    public static IServiceCollection AddSteamConnector(
        this IServiceCollection services, Func<IServiceProvider, SteamOptions> options)
    {
        services.AddSingleton<ILibrarySource>(provider => new SteamLibrarySource(
            provider.GetRequiredService<IHttpClientFactory>(),
            () => options(provider),
            provider.GetRequiredService<TimeProvider>()));
        services
            .AddHttpClient(SteamLibrarySource.HttpClientName, client =>
            {
                client.BaseAddress = SteamLibrarySource.Api;
                // The connector counts the time itself, the body included, and its size too.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(Transport);
        return services;
    }

    /// <summary>How Steam is reached. The key travels in a header: an answer that sends elsewhere is
    /// not followed with it.</summary>
    internal static SocketsHttpHandler Transport() => new() { AllowAutoRedirect = false };
}
