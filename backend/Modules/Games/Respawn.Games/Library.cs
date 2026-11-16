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
public sealed record RawSnapshot(
    Guid Id,
    string Source,
    string Kind,
    DateTimeOffset TakenAt,
    Guid? OwnerId,
    Guid? LinkedAccountId,
    string Payload);

/// <summary>A game a store knows and the catalogue did not: its store entry, and the canonical record
/// created for it.</summary>
public sealed record NewStoreEntry(Guid Id, Guid GameId, string Source, string ExternalId, string Name);

/// <summary>A library as a connector read it: the source's answer untouched, and what it says.</summary>
/// <param name="Kind">Which of the source's answers this is, in the connector's words.</param>
/// <param name="Payload">The answer as received.</param>
/// <param name="Items">The games it lists.</param>
public sealed record LibraryReading(string Kind, string Payload, IReadOnlyList<OwnedItem> Items);

/// <summary>A game of a library, in the store's words.</summary>
/// <param name="ExternalId">The game's id in the store.</param>
/// <param name="Name">Never blank: a connector whose source gives no name decides what stands for it.</param>
/// <param name="Playtime">The total played, as the store counts it.</param>
/// <param name="LastPlayedAt">Null when the store does not say, or the game was never launched.</param>
public sealed record OwnedItem(string ExternalId, string Name, TimeSpan Playtime, DateTimeOffset? LastPlayedAt);

/// <summary>A store entry a linked account owns, and how long it was played there.</summary>
public sealed record Ownership(Guid StoreEntryId, TimeSpan Playtime, DateTimeOffset? LastPlayedAt);

/// <summary>A game of an owner's library: the canonical record, and the store entry it is owned through.</summary>
public sealed record OwnedGame(
    Guid GameId,
    string Name,
    string Source,
    string ExternalId,
    TimeSpan Playtime,
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
