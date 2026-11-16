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

    /// <summary>
    /// Records what a store said of a linked account's library. Returns the snapshot's id.
    ///
    /// Always kept: the answer as a snapshot, dated when the source answered, and what it says of each
    /// game as an observation of that snapshot. The history of a library is read from these.
    ///
    /// Then the library itself, the best that is known, is brought up to date, by these rules.
    /// A game the catalogue did not know is added to it.
    /// A reading older than what is already known of a game changes nothing of it but the date it was
    /// first seen.
    /// A reading that does not say (no playtime, no date, no name) erases nothing, and a playtime of
    /// zero does not replace a known one.
    /// A game an earlier reading listed and this one does not is left as it was.
    /// A store entry takes the name its store now gives it; <see cref="ICatalogue.RenameAsync"/> says
    /// when the game follows.
    /// </summary>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownLinkedAccount"/>.</exception>
    /// <exception cref="ArgumentException">The reading is dated in the future, or malformed.</exception>
    Task<Guid> RecordLibraryAsync(Guid linkedAccountId, LibraryReading reading, CancellationToken ct);

    /// <summary>What an owner owns, through every store account linked to them.</summary>
    Task<IReadOnlyList<OwnedGame>> GetLibraryAsync(Guid ownerId, CancellationToken ct);
}
