namespace Respawn.Games;

// A game's cover. Where a store says its cover is, a short text beside the store entry, is always kept.
// The image itself is kept only by an instance given a data directory (IImageStore): fetched from its
// source the first time it is asked for, then served from the file. Without one, a cover is asked of a
// source each time, and passed on.

/// <summary>A cover as a source gave it: an image, and what kind.</summary>
/// <param name="ContentType">The image's media type, as the connector made sure of it; never the
/// source's word alone.</param>
/// <param name="Bytes">The image.</param>
public sealed record Cover(string ContentType, ReadOnlyMemory<byte> Bytes)
{
    /// <summary>What kind of image these bytes start as, among those passed on: JPEG, PNG or WebP; null
    /// for anything else. Every connector makes sure of a cover this way, whatever its source says.</summary>
    public static string? MediaTypeOf(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith<byte>([0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (bytes.StartsWith<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        return bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)
            ? "image/webp"
            : null;
    }
}

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
public sealed class CoverGate : Gate<Guid>;

/// <summary>The cover the game database gives a game, as far as the catalogue knows it.</summary>
/// <param name="ImageId">What names its cover in the database's image host: that of the first of the
/// game's store entries, in the order covers are looked for, whose database game has one; null when none
/// has.</param>
/// <param name="Asked">Whether the database was asked about every store entry of the game.</param>
/// <param name="SaidAt">When the database last said anything of the game: a store entry asked about, or
/// its database game read; null when it never did.</param>
public sealed record GameDataCover(string? ImageId, bool Asked, DateTimeOffset? SaidAt = null);

/// <summary>What a game has a cover from, as far as the catalogue knows: the stores that said where
/// theirs is, whether the game database gives one, and, where images are kept, whose cover the game
/// shows as its own.</summary>
/// <param name="Stores">The stores that said where their cover is, in no order.</param>
/// <param name="GameData">Whether the game database gives the game a cover.</param>
/// <param name="Shown">The game's own cover as it is kept; null while none is, or on an instance that
/// keeps no image.</param>
public sealed record CoverOrigins(IReadOnlyCollection<StoreCoverSaid> Stores, bool GameData, ShownCover? Shown = null)
{
    public static readonly CoverOrigins None = new([], false);

    /// <summary>The sources, the game's own cover's first, then in the order its own cover is looked
    /// for: the stores by name, then the game database.
    ///
    /// The kept cover is what the game shows, so it says which is first. A store's comes first whether
    /// that store said where its cover is or not: Steam's image host has art for a game whose store names
    /// no place. The game database's is kept only once the stores had none: a store counts again once
    /// it said where its cover is after that, the next look asking it first.</summary>
    public IReadOnlyList<string> Sources(string gameDataSource)
    {
        var shownStore = Shown is { } shown && shown.Source != gameDataSource ? shown.Source : null;
        var stores = Stores
            .Where(store => Shown is not { } own || own.Source != gameDataSource || store.SaidAt > own.CheckedAt)
            .Select(store => store.Source)
            .Where(store => store != shownStore)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        return
        [
            .. shownStore is null ? Array.Empty<string>() : [shownStore],
            .. stores,
            .. GameData || Shown?.Source == gameDataSource ? [gameDataSource] : Array.Empty<string>(),
        ];
    }

    /// <summary>An owner's choice while it still is one: a source the game has a cover from, other than
    /// its own cover's; null otherwise, the game showing its own.</summary>
    public static string? Chosen(IReadOnlyList<string> sources, string? choice) =>
        choice is not null && sources.Skip(1).Contains(choice, StringComparer.Ordinal) ? choice : null;
}

/// <summary>A store that said where a game's cover is.</summary>
/// <param name="Source">The store, by name.</param>
/// <param name="SaidAt">When it last said.</param>
public sealed record StoreCoverSaid(string Source, DateTimeOffset? SaidAt);

/// <summary>The cover a game shows as its own, as it is kept.</summary>
/// <param name="Source">Who gave it: a store, or the game database.</param>
/// <param name="CheckedAt">When its source was last asked for it.</param>
public sealed record ShownCover(string Source, DateTimeOffset CheckedAt);
