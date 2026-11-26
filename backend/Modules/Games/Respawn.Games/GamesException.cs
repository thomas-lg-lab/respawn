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

    /// <summary>No source has a cover for the game. Never thrown: an answer of its own
    /// (<see cref="CoverAnswer"/>), named here for the refusal it becomes.</summary>
    NoCover,

    /// <summary>The owner's library does not have the game.</summary>
    NotInLibrary,

    /// <summary>The game cannot be taken out of the library: its store lists every entry of it.</summary>
    StillListed,

    /// <summary>The owner took nothing of the game out of their library.</summary>
    NotRemoved,

    /// <summary>No store lists again what the owner took out of the game: there is no notice to dismiss.</summary>
    NotBack,
}

public sealed class GamesException(GamesError error) : Exception(error.ToString())
{
    public GamesError Error { get; } = error;
}
