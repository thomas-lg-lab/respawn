namespace Respawn.Games.Steam;

/// <summary>Where the instance's Steam Web API key comes from: the environment, or a file that may
/// appear, and change, after the app has started.</summary>
public sealed class SteamOptions
{
    /// <summary>The most a key file may weigh: a key is 32 characters.</summary>
    private const int MaxFileBytes = 4096;

    public string? ApiKey { get; init; }

    public string ApiKeyFile { get; init; } = "/secrets/steam-api-key";

    /// <summary>Whether this can be a key: Steam's keys are letters and digits. What is not is never
    /// sent anywhere.</summary>
    public static bool IsKey(string value) => value.Length is > 0 and <= 128 && value.All(char.IsAsciiLetterOrDigit);

    /// <summary>What was given as the key, as it is now: read again on each call, nothing is cached.
    /// Null when nothing was given: no variable, and no file, or one that is empty or cannot be a key
    /// file.</summary>
    public string? Resolve()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            return ApiKey.Trim();
        }

        if (string.IsNullOrWhiteSpace(ApiKeyFile))
        {
            return null;
        }

        try
        {
            var file = new FileInfo(ApiKeyFile);
            if (!file.Exists || file.Length > MaxFileBytes)
            {
                return null;
            }

            var text = File.ReadAllText(file.FullName).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
