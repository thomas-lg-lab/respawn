namespace Respawn.Games;

/// <summary>Why a source could not be read.</summary>
public enum SourceError
{
    /// <summary>The instance has no access to the source: no key was given to it.</summary>
    NotConfigured,

    /// <summary>The source refused the instance's access: a key that is wrong, revoked, or not allowed
    /// to ask this.</summary>
    Refused,

    /// <summary>The source did not answer, or asked to come back later. Trying again may work.</summary>
    Unavailable,

    /// <summary>The source answered something the connector does not understand.</summary>
    Unreadable,
}

/// <summary>A source could not be read. The message says what happened in the connector's words, and
/// never holds a secret.</summary>
public sealed class SourceException(SourceError error, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public SourceError Error { get; } = error;
}
