namespace Respawn.Games.Ports;

/// <summary>The covers of the catalogue's games, asked of the sources (<see cref="ICoverSource"/>) each
/// time and never kept: only where a store says a cover is.</summary>
public interface ICoverService
{
    /// <summary>The cover of a game, from the first of its store entries whose source has one.</summary>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownGame"/>: the catalogue has no such
    /// game. <see cref="GamesError.NoCover"/>: no source has a cover for it.</exception>
    /// <exception cref="SourceException">No source gave a cover, and one of them could not be read: the
    /// game may have one.</exception>
    Task<Cover> GetCoverAsync(Guid gameId, CancellationToken ct);

    /// <summary>Asks the store of a linked account where the covers of its games are, for those never
    /// asked about or asked about more than <see cref="CoverSchedule.AskAgainAfter"/> ago, and keeps
    /// the answers as they come. Returns how many games were asked about: none when the account is
    /// gone or no connector reads the covers of its store.</summary>
    /// <exception cref="SourceException">The store could not be read; what it said before is kept.</exception>
    Task<int> ReadCoverPathsAsync(Guid linkedAccountId, CancellationToken ct);
}
