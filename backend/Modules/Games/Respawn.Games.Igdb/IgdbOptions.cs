namespace Respawn.Games.Igdb;

/// <summary>Where the instance's access to IGDB comes from: the client id and secret of a Twitch
/// application, each from the environment or from a file that may appear, and change, after the app has
/// started. Optional: without both, IGDB is never asked anything.</summary>
public sealed class IgdbOptions
{
    /// <summary>The most a file may weigh: Twitch's ids and secrets are 30 characters.</summary>
    private const int MaxFileBytes = 4096;

    public string? ClientId { get; init; }

    public string ClientIdFile { get; init; } = "/secrets/igdb-client-id";

    public string? ClientSecret { get; init; }

    public string ClientSecretFile { get; init; } = "/secrets/igdb-client-secret";

    /// <summary>Whether this can be a client id, a secret or a token of Twitch's: letters and digits.
    /// What is not is never sent anywhere.</summary>
    public static bool IsCredential(string value) => value.Length is > 0 and <= 128 && value.All(char.IsAsciiLetterOrDigit);

    /// <summary>The client id and secret as they are now: read again on each call, nothing is cached.
    /// Null when either was not given: no variable, and no file, or one that is empty or cannot be read.</summary>
    public (string ClientId, string ClientSecret)? Resolve() =>
        Read(ClientId, ClientIdFile) is { } id && Read(ClientSecret, ClientSecretFile) is { } secret ? (id, secret) : null;

    private static string? Read(string? value, string file)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > MaxFileBytes)
            {
                return null;
            }

            var text = File.ReadAllText(info.FullName).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
