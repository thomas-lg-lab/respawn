using Respawn.Accounts;
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
        try
        {
            services.GetRequiredService<HttpSettings>();
            services.GetRequiredService<AccountOptions>();
            images = services.GetRequiredService<IImageStore>();
        }
        catch (FormatException ex)
        {
            // The message names the setting and never quotes its value.
            logger.LogCritical("a setting cannot be read, the app does not start: {Reason}", ex.Message);
            throw;
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
                throw;
            }

            logger.LogInformation("covers are kept in the data directory {Directory}, fetched from their source the first time each is asked for", files.Directory);
        }
        else
        {
            logger.LogInformation("no data directory (RESPAWN_DATA_DIR): covers are not kept, each is fetched from its source when it is asked for");
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
