using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Infrastructure.Credentials;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Games.Ports;
using Respawn.SharedKernel;

namespace Respawn.Games.Infrastructure;

public static class DependencyInjection
{
    /// <summary>The adapters behind the ports of the Games module. The host registers a
    /// <see cref="TimeProvider"/> and the <see cref="IConnectionSource"/>, and says where the
    /// encryption key comes from; nothing is read here, the key is looked for when it is needed.</summary>
    public static IServiceCollection AddGamesInfrastructure(
        this IServiceCollection services, Func<IServiceProvider, EncryptionOptions> encryption)
    {
        services.AddSingleton(provider => new CredentialCipher(() => encryption(provider)));
        services.AddScoped<ICredentialStore, EfCredentialStore>();

        // The module's schema. Nothing connects here: the context's options are built when a request
        // first needs the context, which is when the host opens the connection and migrates.
        services.AddSingleton<ISchemaMigrator, GamesSchemaMigrator>();
        services.AddDbContext<GamesDbContext>((provider, options) =>
            GamesDbContext.Configure(options, provider.GetRequiredService<IConnectionSource>().DataSource()));

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ILinkedAccountStore, EfLinkedAccountStore>();
        services.AddScoped<ISnapshotStore, EfSnapshotStore>();
        services.AddScoped<ISyncRunStore, EfSyncRunStore>();
        services.AddScoped<ICatalogue, EfCatalogue>();
        services.AddScoped<ILibraryStore, EfLibraryStore>();

        return services;
    }
}
