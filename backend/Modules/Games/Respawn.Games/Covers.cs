namespace Respawn.Games;

// A game's cover. No image is ever kept: a cover is asked of a source each time, and passed on. What is
// kept is where a store says its cover is, a short text beside the store entry.

/// <summary>A cover as a source gave it: an image, and what kind.</summary>
/// <param name="ContentType">The image's media type, as the connector made sure of it; never the
/// source's word alone.</param>
/// <param name="Bytes">The image.</param>
public sealed record Cover(string ContentType, ReadOnlyMemory<byte> Bytes);

/// <summary>A store entry of a game, with where its store says its cover is, when it was asked and
/// said.</summary>
public sealed record CoverEntry(Guid StoreEntryId, string Source, string ExternalId, string? CoverPath);

/// <summary>Where a store says a store entry's cover is; null when it knows none.</summary>
public sealed record CoverPath(Guid StoreEntryId, string? Path);

/// <summary>When the stores are asked where their covers are.</summary>
public static class CoverSchedule
{
    /// <summary>How long a store's word on a cover's place is taken before it is asked again: a store
    /// changes a game's artwork now and then, and gets one for a game it had none for.</summary>
    public static readonly TimeSpan AskAgainAfter = TimeSpan.FromDays(30);

    /// <summary>The most time asking a store about a library's covers may take after a sync, however
    /// many games are left: the sync waits for it, and the next one goes on from there.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);
}
