namespace Respawn.Games.Infrastructure.Persistence;

// The rows of the games schema. An owner is an account of the instance: its id is kept, and nothing
// here points into the tables of the module that holds accounts.

/// <summary>A game, once, whatever the store: the canonical record. Its IGDB id when it is known; no
/// name while no source has given one.</summary>
public sealed class GameRow
{
    public Guid Id { get; set; }

    public string? Name { get; set; }

    /// <summary>The store entry the record took its name from, when it was created: the name follows
    /// that entry, and no other that points here since a merge. Whatever names the game otherwise (a
    /// person, a merge, another source) sets it to null, and readings leave the name alone from then
    /// on. Not a foreign key: the two tables would point at each other, and a store entry is never
    /// deleted.</summary>
    public Guid? NameSourceEntryId { get; set; }

    /// <summary>Kept for the merges planned later, when two store entries found to be one IGDB game
    /// become one record: nothing writes or reads it yet, and it is not to be dropped as unused.
    /// Which IGDB game a store entry is lives on the entry (<see cref="StoreEntryRow.IgdbId"/>).</summary>
    public long? IgdbId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A game as one store knows it. It points to one canonical record; several may point to the
/// same one. Its name is the store's latest word.</summary>
public sealed class StoreEntryRow
{
    public Guid Id { get; set; }

    public Guid GameId { get; set; }

    public string Source { get; set; } = "";

    public string ExternalId { get; set; } = "";

    public string? Name { get; set; }

    /// <summary>Where the store says the game's cover is, in the words of the store's connector: a
    /// short text, never an image nor a whole address. Null while the store was not asked, or knows
    /// none.</summary>
    public string? CoverPath { get; set; }

    /// <summary>When the store was last asked where the cover is; null while it never was.</summary>
    public DateTimeOffset? CoverAskedAt { get; set; }

    /// <summary>Which IGDB game this is (<see cref="IgdbGameRow"/>); null while IGDB was not asked, or
    /// does not know it. Several entries may be the same IGDB game: they are not merged for it.</summary>
    public long? IgdbId { get; set; }

    /// <summary>When IGDB was last asked which game this is; null while it never was.</summary>
    public DateTimeOffset? IgdbAskedAt { get; set; }

    /// <summary>When the store was last asked for the tags its players gave the game
    /// (<see cref="StoreEntryTagRow"/>); null while it never was.</summary>
    public DateTimeOffset? TagsAskedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>What IGDB says of one of its games, once for the whole instance, replaced each time it is
/// asked again, and removed once no store entry is this game any more. The store entries that are this
/// game point to it by its id (<see cref="StoreEntryRow.IgdbId"/>), with no foreign key: the two are
/// written together, the row first.</summary>
public sealed class IgdbGameRow
{
    public long IgdbId { get; set; }

    public string? Name { get; set; }

    public string? Slug { get; set; }

    /// <summary>Its page on IGDB.</summary>
    public string? Url { get; set; }

    /// <summary>What names its cover in IGDB's image host.</summary>
    public string? CoverImageId { get; set; }

    public List<string> Genres { get; set; } = [];

    public List<string> Themes { get; set; } = [];

    /// <summary>What it is about, in IGDB's words (English).</summary>
    public string? Summary { get; set; }

    /// <summary>How long players took to finish it, in seconds, rushing, at a normal pace, and doing
    /// everything; null where nobody said.</summary>
    public int? HastilySeconds { get; set; }

    public int? NormallySeconds { get; set; }

    public int? CompletelySeconds { get; set; }

    /// <summary>How many players those figures rest on; null when IGDB gave no figure, or no count.</summary>
    public int? TimeToBeatCount { get; set; }

    /// <summary>The day it first came out, anywhere, as IGDB dates it (UTC); null when it does not say.</summary>
    public DateOnly? ReleasedOn { get; set; }

    /// <summary>The IGDB games it lists as close to this one, in its order: ids of IGDB's, which no row
    /// of this table need hold.</summary>
    public List<long> Similar { get; set; } = [];

    /// <summary>When IGDB said it.</summary>
    public DateTimeOffset ReadAt { get; set; }
}

/// <summary>The name a store gives one of its tags in one language, once for the whole instance, replaced
/// each time the store is asked again.</summary>
public sealed class StoreTagNameRow
{
    public string Source { get; set; } = "";

    /// <summary>When the store gave its names, the same for all of them.</summary>
    public DateTimeOffset ReadAt { get; set; }

    /// <summary>The tag's id in the store.</summary>
    public string TagId { get; set; } = "";

    /// <summary>The language, as the app names it (<see cref="StoreTagSchedule.Languages"/>).</summary>
    public string Language { get; set; } = "";

    public string Name { get; set; } = "";
}

/// <summary>One of the tags a store's players gave a store entry, with its weight there, replaced whole each
/// time the store is asked again. Its name is in <see cref="StoreTagNameRow"/>, by the entry's store and
/// the tag's id, with no foreign key: a tag the names do not hold yet is still the game's.</summary>
public sealed class StoreEntryTagRow
{
    public Guid StoreEntryId { get; set; }

    /// <summary>The tag's id in the entry's store.</summary>
    public string TagId { get; set; } = "";

    /// <summary>How much the store weighs it on the game: only the order means something.</summary>
    public int Weight { get; set; }

    /// <summary>Its place among the entry's tags, the heaviest at 0.</summary>
    public int Rank { get; set; }
}

/// <summary>A store account proven to belong to an owner. A store account has one owner.</summary>
public sealed class LinkedAccountRow
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public string Source { get; set; } = "";

    public string ExternalId { get; set; } = "";

    public string? DisplayName { get; set; }

    public DateTimeOffset LinkedAt { get; set; }

    /// <summary>The owner's own access to the store (a Steam Web API key), sealed by
    /// <see cref="Credentials.CredentialCipher"/>; null when they gave none. Never the clear text.</summary>
    public byte[]? Credential { get; set; }

    /// <summary>When the owner gave it; null when they gave none.</summary>
    public DateTimeOffset? CredentialSetAt { get; set; }
}

/// <summary>A source's answer, untouched and dated. With a linked account when it is personal, and
/// its owner is that account's; without one when it is the instance's.</summary>
public sealed class RawSnapshotRow
{
    public Guid Id { get; set; }

    public string Source { get; set; } = "";

    public string Kind { get; set; } = "";

    /// <summary>When the source answered.</summary>
    public DateTimeOffset TakenAt { get; set; }

    /// <summary>When the answer was stored, which may be much later.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    public Guid? LinkedAccountId { get; set; }

    /// <summary>True when it was taken as the whole of what it lists. For a library, what it leaves out
    /// was then noted as missing, and an answer that lists nothing is never one; a yearly review always
    /// is, with no month too.</summary>
    public bool Complete { get; set; }

    /// <summary>Text, as received: `jsonb` would reorder a document's keys, and a source may answer
    /// something that is not JSON at all.</summary>
    public string Payload { get; set; } = "";
}

/// <summary>What one snapshot said of one store entry, as it said it: null where it said nothing. Never
/// changed afterwards; the history of a library is a query over these rows and their snapshots' dates.</summary>
public sealed class ObservationRow
{
    public Guid SnapshotId { get; set; }

    public Guid StoreEntryId { get; set; }

    /// <summary>The total played as the snapshot said it, a poor answer included.</summary>
    public long? PlaytimeSeconds { get; set; }

    /// <summary>The total played as it was known once the snapshot was read: what it said, or what was
    /// known before when it did not say. What a history takes differences of.</summary>
    public long? KnownPlaytimeSeconds { get; set; }

    /// <summary>What was played lately, over the store's own window, as the snapshot said it.</summary>
    public long? RecentPlaytimeSeconds { get; set; }

    public DateTimeOffset? LastPlayedAt { get; set; }
}

/// <summary>A store entry a linked account owns: the best that is known of it from every snapshot so
/// far, which is not always what the latest one said.</summary>
public sealed class OwnershipRow
{
    public Guid LinkedAccountId { get; set; }

    public Guid StoreEntryId { get; set; }

    public long? PlaytimeSeconds { get; set; }

    public DateTimeOffset? LastPlayedAt { get; set; }

    /// <summary>The date of the earliest snapshot that listed it.</summary>
    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>The date of the latest snapshot that listed it.</summary>
    public DateTimeOffset AsOf { get; set; }

    /// <summary>That latest snapshot.</summary>
    public Guid SnapshotId { get; set; }

    /// <summary>What was played lately, as that latest snapshot said it; never carried over.</summary>
    public long? RecentPlaytimeSeconds { get; set; }

    /// <summary>The date from which it is out of the library, which its owner decides and no snapshot
    /// does; null while it is in it. The row and its history stay.</summary>
    public DateTimeOffset? UnlistedSince { get; set; }

    /// <summary>The date of the earliest complete snapshot that left it out, while none has listed it
    /// since; the game is still in the library, flagged.</summary>
    public DateTimeOffset? MissingSince { get; set; }

    /// <summary>Out of the library: the date of the earliest snapshot that listed it again, while none
    /// has left it out since.</summary>
    public DateTimeOffset? ListedAgainSince { get; set; }

    /// <summary>Out of the library: when its owner dismissed, for good, the notice that it is back.</summary>
    public DateTimeOffset? NoticeDismissedAt { get; set; }
}

/// <summary>A year a store reviewed for a linked account (Steam's Year in Review), kept: its months are
/// in <see cref="MonthlyPlaytimeRow"/>, and its answer is the snapshot.</summary>
public sealed class YearReviewRow
{
    public Guid LinkedAccountId { get; set; }

    public int Year { get; set; }

    public Guid SnapshotId { get; set; }
}

/// <summary>What a review says was played of one store entry in one month, through one linked account.
/// Derived from the year's snapshot, and replaced with it.</summary>
public sealed class MonthlyPlaytimeRow
{
    public Guid LinkedAccountId { get; set; }

    public Guid StoreEntryId { get; set; }

    /// <summary>The first day of the month.</summary>
    public DateOnly Month { get; set; }

    public long PlaytimeSeconds { get; set; }

    public int Sessions { get; set; }

    /// <summary>The review it comes from.</summary>
    public Guid SnapshotId { get; set; }
}

/// <summary>One sync of a linked account, once it has ended: when, why, and how it went. The words are
/// those of <see cref="SyncWords"/>.</summary>
public sealed class SyncRunRow
{
    public Guid Id { get; set; }

    public Guid LinkedAccountId { get; set; }

    /// <summary>`scheduled` or `manual`.</summary>
    public string Trigger { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    /// <summary>`succeeded`, `nothing_visible` or `failed`.</summary>
    public string Outcome { get; set; } = "";

    /// <summary>Why it failed: `not_configured`, `refused`, `unavailable` or `unreadable`. Null unless
    /// it did.</summary>
    public string? Error { get; set; }

    /// <summary>What happened, in the connector's words, when it failed.</summary>
    public string? Detail { get; set; }

    /// <summary>The source's answer, when there was one.</summary>
    public Guid? SnapshotId { get; set; }

    /// <summary>True when it read, or was to read, with the access the owner gave on the account
    /// rather than the instance's.</summary>
    public bool OwnCredential { get; set; }
}

/// <summary>The image a game shows for a kind in a slot, kept as a file of the data directory, named by
/// its kind, its source and its key (<see cref="ImageName"/>), never by the game. Only on an instance
/// that keeps images.</summary>
public sealed class ImageRow
{
    public Guid GameId { get; set; }

    public string Kind { get; set; } = "";

    /// <summary>Empty for the game's own image; a source's name for that source's, asked for by name
    /// (<see cref="ImageSlots"/>).</summary>
    public string Slot { get; set; } = "";

    public string Source { get; set; } = "";

    public string Key { get; set; } = "";

    public string ContentType { get; set; } = "";

    /// <summary>When its source was last asked for it.</summary>
    public DateTimeOffset CheckedAt { get; set; }
}

/// <summary>The cover an owner chose for a game, by its source: theirs alone, every other account
/// seeing the game's own.</summary>
public sealed class CoverChoiceRow
{
    public Guid OwnerId { get; set; }

    public Guid GameId { get; set; }

    public string Source { get; set; } = "";

    public DateTimeOffset ChosenAt { get; set; }
}
