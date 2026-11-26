namespace Respawn.Games.Ports;

/// <summary>The covers of the catalogue's games, asked of the sources (<see cref="ICoverSource"/>) each
/// time and never kept: only where a store says a cover is. Apart from the sync: whatever happens
/// here, the sync goes on.</summary>
public interface ICoverService
{
    /// <summary>The cover of a game, from the first of its store entries whose source has one, or
    /// none; and whether that answer is settled (<see cref="CoverAnswer.Settled"/>).</summary>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownGame"/>: the catalogue has no such
    /// game.</exception>
    /// <exception cref="SourceException">No source gave a cover, and one of them could not be read: the
    /// game may have one.</exception>
    Task<CoverAnswer> GetCoverAsync(Guid gameId, CancellationToken ct);

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
