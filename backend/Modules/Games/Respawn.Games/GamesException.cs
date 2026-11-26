namespace Respawn.Games;

/// <summary>Why a request to the module was refused.</summary>
public enum GamesError
{
    /// <summary>The store account is already linked, to another account of the instance.</summary>
    LinkedToAnotherAccount,

    UnknownLinkedAccount,

    /// <summary>A link was asked for from another site's page.</summary>
    CrossSiteLink,

    /// <summary>The linked account is being read right now.</summary>
    SyncInProgress,

    /// <summary>The linked account was read a moment ago.</summary>
    SyncTooSoon,

    /// <summary>What was given as a key of the store is not one, or the store takes none.</summary>
    InvalidCredential,

    /// <summary>The instance cannot keep a key: it was given no encryption key, or one it cannot use.</summary>
    CredentialsUnavailable,

    /// <summary>The catalogue has no such game.</summary>
    UnknownGame,

    /// <summary>No source has a cover for the game.</summary>
    NoCover,
}

public sealed class GamesException(GamesError error) : Exception(error.ToString())
{
    public GamesError Error { get; } = error;
}
