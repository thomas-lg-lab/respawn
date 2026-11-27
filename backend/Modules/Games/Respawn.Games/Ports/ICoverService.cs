namespace Respawn.Games.Ports;

/// <summary>The covers of the catalogue's games, asked of the sources (<see cref="ICoverSource"/>) and,
/// on an instance that keeps images (<see cref="IImageStore"/>), kept and served from there; and where a
/// store says a cover is. Apart from the sync: whatever happens here, the sync goes on.</summary>
public interface ICoverService
{
    /// <summary>The cover of a game, from the first of its store entries whose source has one, or
    /// none; and whether that answer is settled (<see cref="CoverAnswer.Settled"/>). Where images are
    /// kept: the kept one while it is current, asked of its source once its store said again where it
    /// is or <see cref="CoverSchedule.AskAgainAfter"/> after it was last asked for, and still served
    /// when that source does not answer or has none any more. A cover found before the store said
    /// where it is is passed on and not kept.
    ///
    /// With a source, the cover of that source alone, a store or the game database, kept in a slot of
    /// its own (<see cref="ImageSlots"/>) and looked for again the same way. A source that has no cover
    /// for the game, that the instance does not read, or that does not answer with nothing of it kept,
    /// gives the game's own cover instead, never settled: the source's may come.</summary>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownGame"/>: the catalogue has no such
    /// game.</exception>
    /// <exception cref="SourceException">No source gave a cover, and one of them could not be read: the
    /// game may have one.</exception>
    Task<CoverAnswer> GetCoverAsync(Guid gameId, string? source, CancellationToken ct);

    /// <summary>The linked accounts whose store is to ask where covers are, the earliest link first:
    /// those a connector reads the covers of, owning a game never asked about or asked about more
    /// than <see cref="CoverSchedule.AskAgainAfter"/> ago, and not tried less than
    /// <see cref="CoverSchedule.RetryAfter"/> ago in this process. A newly linked account is due once
    /// its first sync listed its games.</summary>
    Task<IReadOnlyList<Guid>> GetDueAsync(CancellationToken ct);

    /// <summary>Asks the store of a linked account where the covers of its games are, for the games
    /// that are due, and keeps the answers batch by batch. A game an answer leaves out while naming
    /// others is kept as one the store does not know; an answer that names none is a store that could
    /// not be read, and nothing is kept of it. A store that cannot be read stops the run there.
    /// Null when there is nothing to do: the account is gone, no connector reads the covers of its
    /// store, it is being asked right now, or was tried less than
    /// <see cref="CoverSchedule.RetryAfter"/> ago.</summary>
    Task<CoverPathsRead?> ReadCoverPathsAsync(Guid linkedAccountId, CancellationToken ct);
}
