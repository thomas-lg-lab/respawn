namespace Respawn.Games.Ports;

/// <summary>The library use cases. Every refusal is a <see cref="GamesException"/>.</summary>
public interface ILibraryService
{
    /// <summary>Links a store account to an owner. The caller has proven the store account is the
    /// owner's. Linking it again changes nothing.</summary>
    /// <exception cref="GamesException"><see cref="GamesError.LinkedToAnotherAccount"/>: a store account
    /// has one owner.</exception>
    Task<LinkedAccount> LinkAccountAsync(
        Guid ownerId, string source, string externalId, string? displayName, CancellationToken ct);

    /// <summary>Records what a store said of a linked account's library: the answer is kept as a dated
    /// snapshot, the games the catalogue did not know are added to it, and ownership and playtime are
    /// brought to what the answer says. A game an earlier reading listed and this one does not is left
    /// as it was. Returns the snapshot's id.</summary>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownLinkedAccount"/>.</exception>
    Task<Guid> RecordLibraryAsync(Guid linkedAccountId, LibraryReading reading, CancellationToken ct);

    /// <summary>What an owner owns, through every store account linked to them.</summary>
    Task<IReadOnlyList<OwnedGame>> GetLibraryAsync(Guid ownerId, CancellationToken ct);
}
