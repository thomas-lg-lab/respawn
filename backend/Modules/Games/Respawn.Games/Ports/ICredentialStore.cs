namespace Respawn.Games.Ports;

/// <summary>The access owners give to read their own store accounts with (a Steam Web API key), one per
/// linked account. Kept encrypted, and gone with the linked account. What it holds is handed back only
/// to the sync: never to a caller of the API, and never written in a log.</summary>
public interface ICredentialStore
{
    /// <summary>Gives a linked account its owner's access, in place of the one it had. False when there
    /// is no such linked account.</summary>
    /// <exception cref="CredentialException">The instance cannot keep it.</exception>
    Task<bool> SetAsync(Guid linkedAccountId, string credential, DateTimeOffset at, CancellationToken ct);

    /// <summary>Takes a linked account's own access away: it is read with the instance's again. False
    /// when there is no such linked account; true when it had none.</summary>
    Task<bool> RemoveAsync(Guid linkedAccountId, CancellationToken ct);

    /// <summary>The access the owner gave on a linked account; null when they gave none.</summary>
    /// <exception cref="CredentialException">One was given, and cannot be read back.</exception>
    Task<string?> FindAsync(Guid linkedAccountId, CancellationToken ct);
}
