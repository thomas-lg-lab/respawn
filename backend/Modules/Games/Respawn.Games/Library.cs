namespace Respawn.Games;

// The words of the module. A game is one canonical record whatever the store; a store entry is that
// game as one store knows it, and points to one canonical record. An owner is an account of the
// instance, known here by its id only.

/// <summary>A store account proven to belong to an account of the instance.</summary>
public sealed record LinkedAccount(
    Guid Id,
    Guid OwnerId,
    string Source,
    string ExternalId,
    string? DisplayName,
    DateTimeOffset LinkedAt);

/// <summary>What a source answered, kept as it came and dated: a field can be read from it again later
/// without asking the source. Personal when it has an owner (a library), the instance's otherwise.</summary>
/// <param name="Id">The snapshot.</param>
/// <param name="Source">The source that answered.</param>
/// <param name="Kind">Which of the source's answers this is.</param>
/// <param name="TakenAt">When the source answered: the date the answer is true for.</param>
/// <param name="RecordedAt">When it was stored here, which may be later: a retry, an import of the past.</param>
/// <param name="OwnerId">The account it is about, when personal.</param>
/// <param name="LinkedAccountId">The store account it was read from, when personal.</param>
/// <param name="Payload">The answer as received.</param>
public sealed record RawSnapshot(
    Guid Id,
    string Source,
    string Kind,
    DateTimeOffset TakenAt,
    DateTimeOffset RecordedAt,
    Guid? OwnerId,
    Guid? LinkedAccountId,
    string Payload);

/// <summary>A store entry the catalogue has, with the name its store last gave it, if any.</summary>
public sealed record StoreEntry(Guid Id, string? Name);

/// <summary>A game a store knows and the catalogue did not: its store entry, and the canonical record
/// created for it, under the store's name.</summary>
public sealed record NewStoreEntry(Guid Id, Guid GameId, string Source, string ExternalId, string? Name);

/// <summary>A store entry whose store now gives another name than the one kept.</summary>
public sealed record StoreEntryRename(Guid Id, string? Was, string Name);

/// <summary>A library as a connector read it: the source's answer untouched, and what it says.</summary>
/// <param name="Kind">Which of the source's answers this is, in the connector's words.</param>
/// <param name="TakenAt">When the source answered, not when the reading is handed over.</param>
/// <param name="Payload">The answer as received.</param>
/// <param name="Items">The games it lists.</param>
public sealed record LibraryReading(string Kind, DateTimeOffset TakenAt, string Payload, IReadOnlyList<OwnedItem> Items);

/// <summary>A game of a library, in the store's words. What the store does not say is null, never a
/// value standing for it: an answer may be poorer than the one before.</summary>
/// <param name="ExternalId">The game's id in the store.</param>
/// <param name="Name">Null when the store gives none.</param>
/// <param name="Playtime">The total played, as the store counts it; null when it does not say.</param>
/// <param name="LastPlayedAt">Null when the store does not say, or the game was never launched.</param>
public sealed record OwnedItem(string ExternalId, string? Name, TimeSpan? Playtime, DateTimeOffset? LastPlayedAt);

/// <summary>What one snapshot said of one store entry, as it said it: the history of a library.</summary>
public sealed record Observation(Guid StoreEntryId, TimeSpan? Playtime, DateTimeOffset? LastPlayedAt);

/// <summary>A store entry a linked account owns: the best that is known of it, from every reading so
/// far.</summary>
/// <param name="StoreEntryId">The store entry.</param>
/// <param name="Playtime">The total played; null while no reading said.</param>
/// <param name="LastPlayedAt">Null while no reading said.</param>
/// <param name="FirstSeenAt">The date of the earliest reading that listed it.</param>
/// <param name="AsOf">The date of the latest reading that listed it.</param>
/// <param name="SnapshotId">That latest reading's snapshot.</param>
public sealed record Ownership(
    Guid StoreEntryId,
    TimeSpan? Playtime,
    DateTimeOffset? LastPlayedAt,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset AsOf,
    Guid SnapshotId);

/// <summary>A game of an owner's library: the canonical record, and the store entry it is owned through.
/// The name is null while no source has named the game.</summary>
public sealed record OwnedGame(
    Guid GameId,
    string? Name,
    string Source,
    string ExternalId,
    TimeSpan? Playtime,
    DateTimeOffset? LastPlayedAt);

/// <summary>The sizes the catalogue accepts.</summary>
public static class Limits
{
    public const int Source = 32;

    public const int ExternalId = 128;

    public const int Name = 500;

    public const int DisplayName = 200;

    public const int SnapshotKind = 64;
}
