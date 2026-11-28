namespace Respawn.Games.Ports;

/// <summary>The tags the stores' players gave the catalogue's games (<see cref="IStoreTagSource"/>), asked on
/// a schedule of their own and kept once for the whole instance. Apart from the sync: whatever happens here,
/// the sync goes on.</summary>
public interface IStoreTagService
{
    /// <summary>The stores to ask: those with a connector, with a store entry never asked for its tags or
    /// asked longer ago than <see cref="StoreTagSchedule.AskAgainAfter"/>, and that could be read at their
    /// last turn or more than <see cref="StoreTagSchedule.RetryAfter"/> ago in this process.</summary>
    Task<IReadOnlyList<string>> GetDueAsync(CancellationToken ct);

    /// <summary>Asks a store for the tags of its entries that are due, at most
    /// <see cref="StoreTagSchedule.EntriesPerTurn"/> of them, never asked first, and for the names of its
    /// tags in every language of <see cref="StoreTagSchedule.Languages"/>. Kept batch by batch, each in its
    /// own transaction: the answers as snapshots, the names, and each entry's tags in the place of those it
    /// had, none included; an entry the answer leaves out keeps its tags and waits its next time. A store that cannot be read stops the run there, and is left alone for
    /// <see cref="StoreTagSchedule.RetryAfter"/>; so is one that fails otherwise, which is thrown. Null when
    /// there is nothing to do: no connector for that store, a turn already on its way, one that failed less
    /// than <see cref="StoreTagSchedule.RetryAfter"/> ago, or nothing due.</summary>
    Task<StoreTagsRead?> ReadAsync(string store, CancellationToken ct);

    /// <summary>Every tag the catalogue holds a name of, with its names, in no order.</summary>
    Task<IReadOnlyList<NamedStoreTag>> GetNamesAsync(CancellationToken ct);
}
