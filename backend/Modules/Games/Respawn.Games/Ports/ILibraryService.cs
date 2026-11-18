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

    /// <summary>The store accounts linked to an owner, the earliest link first.</summary>
    Task<IReadOnlyList<LinkedAccount>> GetLinkedAccountsAsync(Guid ownerId, CancellationToken ct);

    /// <summary>Unlinks a store account from its owner, and forgets everything that was read from it:
    /// the library it gave, its history and the source's answers. Nothing of it is kept, so nothing of
    /// it can ever be shown to whoever links that store account next, and linking it again starts from
    /// nothing. The games stay in the catalogue.</summary>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownLinkedAccount"/>: no such linked
    /// account, or not this owner's; the two are not told apart.</exception>
    Task UnlinkAccountAsync(Guid ownerId, Guid linkedAccountId, CancellationToken ct);

    /// <summary>
    /// Records what a store said of a linked account's library. Returns the snapshot's id.
    ///
    /// Always kept: the answer as a snapshot, dated when the source answered, and what it says of each
    /// game as an observation of that snapshot, beside the total that was known once it was read. The
    /// history of a library is read from these.
    ///
    /// Then the library itself, the best that is known, is brought up to date, by these rules.
    /// A game the catalogue did not know is added to it.
    /// A reading older than what is already known of a game changes nothing of it but the date it was
    /// first seen.
    /// A reading that does not say (no playtime, no date, no name) erases nothing, and a playtime of
    /// zero does not replace a known one.
    /// A game an earlier reading listed and this one does not is left as it was, unless the reading is
    /// the reading of the day, is complete (<see cref="LibraryReading.Complete"/>) and lists at least
    /// one game. The first such reading only notes the game as missing; when a second one leaves it
    /// out too, with no reading that listed it in between, the game is marked as gone since the first
    /// and leaves the library, with what was known of it and its history kept; a later reading that
    /// lists it brings it back. One answer may be partial and still agree with itself: it takes two,
    /// on two days. A reading that is not the day's (its owner asked for it, twice in a minute
    /// perhaps) never tells that a game is gone.
    /// What was played lately is the latest reading's word, or nothing: a window that moves is not
    /// carried over.
    /// A store entry takes the name its store now gives it; <see cref="ICatalogue.RenameAsync"/> says
    /// when the game follows.
    /// </summary>
    /// <param name="linkedAccountId">The store account the library was read from.</param>
    /// <param name="reading">What the store said.</param>
    /// <param name="daily">True for the reading of the day, which the schedule makes once a day.</param>
    /// <param name="ct">Gives up.</param>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownLinkedAccount"/>.</exception>
    /// <exception cref="ArgumentException">The reading is dated in the future, or malformed.</exception>
    /// <remarks>Recording the same answer twice keeps it twice: totals are cumulative, so a history
    /// reads no playtime between the two.</remarks>
    Task<Guid> RecordLibraryAsync(Guid linkedAccountId, LibraryReading reading, bool daily, CancellationToken ct);

    /// <summary>What an owner owns today, through every store account linked to them: one item per
    /// game, in the order asked, and the part of it from <paramref name="offset"/> on.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A negative offset, or a limit outside 1 to
    /// <see cref="Limits.LibraryPage"/>.</exception>
    Task<LibraryPage> GetLibraryAsync(Guid ownerId, LibrarySort sort, int offset, int limit, CancellationToken ct);
}
