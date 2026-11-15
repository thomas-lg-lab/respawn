using Microsoft.Extensions.DependencyInjection;
using Respawn.SharedKernel;

namespace Respawn.Persistence;

public static class DatabaseServices
{
    /// <summary>The database every module shares. Nothing connects here: a module's first query does,
    /// and every registered <see cref="ISchemaMigrator"/> runs then. The host says where the connection
    /// string comes from and registers a <see cref="TimeProvider"/>.</summary>
    public static IServiceCollection AddDatabase(
        this IServiceCollection services, Func<IServiceProvider, DatabaseOptions> options)
    {
        services.AddSingleton(options);
        services.AddSingleton<Database>();
        services.AddSingleton<IConnectionSource>(provider => provider.GetRequiredService<Database>());
        return services;
    }
}
