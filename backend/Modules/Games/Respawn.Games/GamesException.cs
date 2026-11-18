namespace Respawn.Games;

/// <summary>Why a request to the module was refused.</summary>
public enum GamesError
{
    /// <summary>The store account is already linked, to another account of the instance.</summary>
    LinkedToAnotherAccount,

    UnknownLinkedAccount,

    /// <summary>A link was asked for from another site's page.</summary>
    CrossSiteLink,
}

public sealed class GamesException(GamesError error) : Exception(error.ToString())
{
    public GamesError Error { get; } = error;
}
