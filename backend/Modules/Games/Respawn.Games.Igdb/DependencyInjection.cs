using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Ports;

namespace Respawn.Games.Igdb;

public static class DependencyInjection
{
    /// <summary>The IGDB connector, behind <see cref="IGameDataSource"/>.
    /// Nothing is read here: the client id and secret are looked for when IGDB is asked something, and an
    /// instance given none never asks it. The host says where they come from and registers a
    /// <see cref="TimeProvider"/>.</summary>
    public static IServiceCollection AddIgdbConnector(
        this IServiceCollection services, Func<IServiceProvider, IgdbOptions> options)
    {
        services.AddSingleton(provider => new IgdbClient(
            provider.GetRequiredService<IHttpClientFactory>(),
            () => options(provider),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new IgdbImages(provider.GetRequiredService<IHttpClientFactory>()));
        services.AddSingleton<IGameDataSource>(provider => new IgdbGameDataSource(
            provider.GetRequiredService<IgdbClient>(),
            provider.GetRequiredService<IgdbImages>(),
            provider.GetRequiredService<TimeProvider>()));

        // A client per host: the one that carries the client id and secret to Twitch, the one that
        // carries the token to IGDB, and the one that asks the image host, which carries neither.
        foreach (var (name, address, transport) in new (string, Uri, Func<HttpMessageHandler>)[]
        {
            (IgdbClient.TokenClientName, IgdbClient.Twitch, Transport),
            (IgdbClient.HttpClientName, IgdbClient.Api, Transport),
            (IgdbImages.ClientName, IgdbImages.Host, ImagesTransport),
        })
        {
            services
                .AddHttpClient(name, client =>
                {
                    client.BaseAddress = address;
                    // The connector counts the time itself, the body included, and its size too.
                    client.Timeout = Timeout.InfiniteTimeSpan;
                })
                .ConfigurePrimaryHttpMessageHandler(transport);
        }

        return services;
    }

    /// <summary>How the image host is reached: no redirect followed either, an image being looked for
    /// on that host only.</summary>
    internal static SocketsHttpHandler ImagesTransport() =>
        new() { AllowAutoRedirect = false, MaxConnectionsPerServer = IgdbImages.MaxConnections };

    /// <summary>How IGDB and Twitch are reached. An answer that sends elsewhere is not followed: the
    /// secret and the token go to the host asked, never to another.</summary>
    internal static SocketsHttpHandler Transport() =>
        new() { AllowAutoRedirect = false, MaxConnectionsPerServer = IgdbClient.MaxOpen };
}
