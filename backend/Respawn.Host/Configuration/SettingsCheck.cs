using Respawn.Accounts;

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
        try
        {
            services.GetRequiredService<HttpSettings>();
            services.GetRequiredService<AccountOptions>();
        }
        catch (FormatException ex)
        {
            // The message names the setting and never quotes its value.
            logger.LogCritical("a setting cannot be read, the app does not start: {Reason}", ex.Message);
            throw;
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
