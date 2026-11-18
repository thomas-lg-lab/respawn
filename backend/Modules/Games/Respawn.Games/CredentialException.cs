namespace Respawn.Games;

/// <summary>An owner's access to a store cannot be kept, or cannot be read back: the instance has no
/// encryption key, one it cannot use, or not the one the access was kept with. The message says which,
/// and never holds a secret.</summary>
public sealed class CredentialException(string message, Exception? inner = null) : Exception(message, inner);
