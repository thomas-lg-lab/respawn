using Respawn.Accounts;
using Respawn.Games;
using Respawn.Games.Infrastructure.Images;
using Respawn.Games.Ports;

namespace Respawn.Host.Configuration;

/// <summary>
/// Reads every setting once, before the app listens: one that cannot be read stops the start, with a
/// line that names it. Left to the first request that needs it, a typo would start an app that answers
/// 500 on every sign-in, or not start it at all, depending on the setting.
///
/// Only what is written is checked. What is absent has a default, and no secret is read here: the app
/// still starts with no database and no key.
/// </summary>
internal sealed class SettingsCheck(IServiceProvider services, ILogger<SettingsCheck> logger) : IHostedLifecycleService
{
    /// <summary>Before anything starts: the web server reads the HTTP settings when it does, and would
    /// fail there first, without the line below.</summary>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        IImageStore images;
        GameDataOptions gameData;
        try
        {
            services.GetRequiredService<HttpSettings>();
            services.GetRequiredService<AccountOptions>();
            images = services.GetRequiredService<IImageStore>();
            gameData = services.GetRequiredService<GameDataOptions>();
        }
        catch (FormatException ex)
        {
            // The message names the setting and never quotes its value.
            logger.LogCritical("a setting cannot be read, the app does not start: {Reason}", ex.Message);
            throw new StartRefusedException("a setting cannot be read", ex);
        }

        // Said once: where the covers come from, for whoever runs the instance.
        if (images is FileImageStore files)
        {
            try
            {
                files.Prepare();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogCritical("the data directory (RESPAWN_DATA_DIR) cannot be written, the app does not start: {Reason}", ex.Message);
                throw new StartRefusedException("the data directory cannot be written", ex);
            }

            logger.LogInformation("covers are kept in the data directory {Directory}, fetched from their source the first time each is asked for", files.Directory);
        }
        else
        {
            logger.LogInformation("no data directory (RESPAWN_DATA_DIR): covers are not kept, each is fetched from its source when it is asked for");
        }

        // Said once too: whether the games are asked about of IGDB. Its client id and secret are read
        // again at each turn, so access given later is taken then, and not said.
        if (services.GetRequiredService<IGameDataSource>().HasAccess)
        {
            logger.LogInformation("IGDB is asked about the games, {GamesPerTurn} at most every few minutes", gameData.GamesPerTurn);
        }
        else
        {
            logger.LogInformation("no IGDB client id and secret (RESPAWN_IGDB_CLIENT_ID, RESPAWN_IGDB_CLIENT_SECRET): IGDB is not asked about the games");
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
