namespace Respawn.Games.Infrastructure.Credentials;

/// <summary>Where the instance's encryption key comes from: the environment, or a file that may appear,
/// and change, after the app has started. It seals what owners give to read their own store accounts
/// with; without it, nobody can give one, and the instance's own access is the only one.</summary>
public sealed class EncryptionOptions
{
    /// <summary>The length of the key: 32 bytes, written in base64.</summary>
    public const int KeyBytes = 32;

    /// <summary>The most a key file may weigh: a key is 44 characters.</summary>
    private const int MaxFileBytes = 4096;

    public string? Key { get; init; }

    public string KeyFile { get; init; } = "/secrets/encryption-key";

    /// <summary>The key, as it is now: read again on each call, nothing is cached.</summary>
    /// <exception cref="CredentialException">No key was given, or what was given is not 32 bytes in
    /// base64. The message never quotes it.</exception>
    public byte[] Resolve()
    {
        var text = Given() ?? throw new CredentialException("the instance has no encryption key");
        var key = new byte[KeyBytes];
        if (!Convert.TryFromBase64String(text, key, out var written) || written != KeyBytes)
        {
            throw new CredentialException($"what the instance was given as its encryption key is not {KeyBytes} bytes in base64");
        }

        return key;
    }

    private string? Given()
    {
        if (!string.IsNullOrWhiteSpace(Key))
        {
            return Key.Trim();
        }

        if (string.IsNullOrWhiteSpace(KeyFile))
        {
            return null;
        }

        try
        {
            var file = new FileInfo(KeyFile);
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
