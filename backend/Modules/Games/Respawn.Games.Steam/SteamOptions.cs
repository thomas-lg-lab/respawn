namespace Respawn.Games.Steam;

/// <summary>Where the instance's Steam Web API key comes from: the environment, or a file that may
/// appear, and change, after the app has started.</summary>
public sealed class SteamOptions
{
    public string? ApiKey { get; init; }

    public string ApiKeyFile { get; init; } = "/secrets/steam-api-key";

    /// <summary>The key as it is now; read again on each call, nothing is cached. Null when there is
    /// none, or when what is there cannot be a key: Steam's keys are letters and digits.</summary>
    public string? Resolve()
    {
        var key = ApiKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            try
            {
                key = File.ReadAllText(ApiKeyFile).Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return key.Length is > 0 and <= 128 && key.All(char.IsAsciiLetterOrDigit) ? key : null;
    }
}
