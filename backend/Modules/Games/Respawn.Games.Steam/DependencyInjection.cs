using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Ports;

namespace Respawn.Games.Steam;

public static class DependencyInjection
{
    /// <summary>The Steam connector, behind <see cref="ILibrarySource"/>, <see cref="IYearReviewSource"/>
    /// and <see cref="ICoverSource"/>.
    /// Nothing is read here: the key is looked for when Steam is asked something. The host says where the
    /// key comes from and registers a <see cref="TimeProvider"/>.</summary>
    public static IServiceCollection AddSteamConnector(
        this IServiceCollection services, Func<IServiceProvider, SteamOptions> options)
    {
        services.AddSingleton(provider => new SteamClient(
            provider.GetRequiredService<IHttpClientFactory>(),
            () => options(provider),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<ILibrarySource>(provider => new SteamLibrarySource(provider.GetRequiredService<SteamClient>()));
        services.AddSingleton<IYearReviewSource>(provider => new SteamYearReviewSource(provider.GetRequiredService<SteamClient>()));
        services.AddSingleton<ICoverSource>(provider => new SteamCoverSource(provider.GetRequiredService<SteamClient>()));

        // A client per use: the one that carries a key, and the two that never do.
        foreach (var (name, address) in new[]
        {
            (SteamLibrarySource.HttpClientName, SteamLibrarySource.Api),
            (SteamCoverSource.StoreClientName, SteamLibrarySource.Api),
            (SteamCoverSource.ImagesClientName, SteamCoverSource.Images),
        })
        {
            services
                .AddHttpClient(name, client =>
                {
                    client.BaseAddress = address;
                    // The connector counts the time itself, the body included, and its size too.
                    client.Timeout = Timeout.InfiniteTimeSpan;
                })
                .ConfigurePrimaryHttpMessageHandler(Transport);
        }

        return services;
    }

    /// <summary>How Steam is reached. The key travels in a header: an answer that sends elsewhere is
    /// not followed with it, and an image is taken from the host asked, never from another.</summary>
    internal static SocketsHttpHandler Transport() => new() { AllowAutoRedirect = false };
}
