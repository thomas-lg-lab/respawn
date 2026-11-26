namespace Respawn.Games;

// A game's cover. Where a store says its cover is, a short text beside the store entry, is always kept.
// The image itself is kept only by an instance given a data directory (IImageStore): fetched from its
// source the first time it is asked for, then served from the file. Without one, a cover is asked of a
// source each time, and passed on.

/// <summary>A cover as a source gave it: an image, and what kind.</summary>
/// <param name="ContentType">The image's media type, as the connector made sure of it; never the
/// source's word alone.</param>
/// <param name="Bytes">The image.</param>
public sealed record Cover(string ContentType, ReadOnlyMemory<byte> Bytes);

/// <summary>A cover as the route sends it.</summary>
/// <param name="ContentType">The image's media type.</param>
/// <param name="Content">The image, from a kept file or from what a source just gave; the reader
/// disposes of it.</param>
/// <param name="Tag">What names this image and no other, for a browser to ask whether it changed; null
/// for an image that is not kept.</param>
public sealed record CoverImage(string ContentType, Stream Content, string? Tag);

/// <summary>What asking for a game's cover gave.</summary>
/// <param name="Cover">The cover; null when no source has one and none is kept.</param>
/// <param name="Settled">True when the answer stands on a store's word about where the cover is: it
/// will not change before that store is asked again. False while a store was not asked yet: a cover
/// found meanwhile may not be the right image, and a cover not found may be found once it is.</param>
public sealed record CoverAnswer(CoverImage? Cover, bool Settled);

/// <summary>A store entry of a game, with where its store says its cover is, when it was asked and
/// said.</summary>
/// <param name="StoreEntryId">The store entry.</param>
/// <param name="Source">Its store.</param>
/// <param name="ExternalId">The game's id in the store.</param>
/// <param name="CoverPath">Where the store said the cover is; null when it was not asked, or said none.</param>
/// <param name="AskedAt">When the store was last asked where the cover is; null while it never was.</param>
public sealed record CoverEntry(Guid StoreEntryId, string Source, string ExternalId, string? CoverPath, DateTimeOffset? AskedAt = null)
{
    /// <summary>Whether the store was ever asked where the cover is.</summary>
    public bool Asked => AskedAt is not null;
}

/// <summary>Where a store says a store entry's cover is; null when it knows none.</summary>
public sealed record CoverPath(Guid StoreEntryId, string? Path);

/// <summary>How asking a store about the covers of a linked account's games ended.</summary>
/// <param name="Learnt">The games whose cover's place, or lack of one, was kept by this run.</param>
/// <param name="Error">Why the store could not be read, when it could not: the games left are asked
/// about at the next attempt.</param>
/// <param name="Detail">What happened, in the connector's words.</param>
public sealed record CoverPathsRead(int Learnt, SourceError? Error = null, string? Detail = null);

/// <summary>When the stores are asked where their covers are. Apart from the sync: it only needs the
/// games a sync listed, and nothing of the sync waits for it.</summary>
public static class CoverSchedule
{
    /// <summary>How long a store's word on a cover's place is taken before it is asked again: a store
    /// changes a game's artwork now and then, and gets one for a game it had none for.</summary>
    public static readonly TimeSpan AskAgainAfter = TimeSpan.FromDays(30);

    /// <summary>How long a linked account's store is left alone after it was asked, whatever came of
    /// it: a store that fails is not asked again at every turn.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);
}

/// <summary>The questions on their way, in this process, and when each linked account's store was last
/// asked: one at a time per account, and not again before <see cref="CoverSchedule.RetryAfter"/>. A
/// restart forgets it, and asks once more.</summary>
public sealed class CoverGate
{
    private readonly HashSet<Guid> _running = [];
    private readonly Dictionary<Guid, DateTimeOffset> _tried = [];

    public bool TryEnter(Guid linkedAccountId)
    {
        lock (_running)
        {
            return _running.Add(linkedAccountId);
        }
    }

    public void Leave(Guid linkedAccountId)
    {
        lock (_running)
        {
            _running.Remove(linkedAccountId);
        }
    }

    public DateTimeOffset? TriedAt(Guid linkedAccountId)
    {
        lock (_running)
        {
            return _tried.TryGetValue(linkedAccountId, out var at) ? at : null;
        }
    }

    public void Tried(Guid linkedAccountId, DateTimeOffset at)
    {
        lock (_running)
        {
            _tried[linkedAccountId] = at;
        }
    }
}
