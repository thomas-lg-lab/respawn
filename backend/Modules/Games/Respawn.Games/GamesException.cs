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
}

public sealed class GamesException(GamesError error) : Exception(error.ToString())
{
    public GamesError Error { get; } = error;
}
