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
                client.Timeout = TimeSpan.FromSeconds(30);
                client.MaxResponseContentBufferSize = SteamLibrarySource.MaxAnswerBytes;
            })
            // The key travels in a header: an answer that sends elsewhere is not followed with it.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        return services;
    }
}
