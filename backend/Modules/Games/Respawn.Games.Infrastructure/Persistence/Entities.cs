namespace Respawn.Games.Infrastructure.Persistence;

// The rows of the games schema. An owner is an account of the instance: its id is kept, and nothing
// here points into the tables of the module that holds accounts.

/// <summary>A game, once, whatever the store: the canonical record. Its IGDB id when it is known.</summary>
public sealed class GameRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public long? IgdbId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A game as one store knows it. It points to one canonical record; several may point to the
/// same one.</summary>
public sealed class StoreEntryRow
{
    public Guid Id { get; set; }

    public Guid GameId { get; set; }

    public string Source { get; set; } = "";

    public string ExternalId { get; set; } = "";

    public string Name { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
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
}

/// <summary>A source's answer, untouched and dated. With an owner and a linked account when it is
/// personal; with neither when it is the instance's.</summary>
public sealed class RawSnapshotRow
{
    public Guid Id { get; set; }

    public string Source { get; set; } = "";

    public string Kind { get; set; } = "";

    public DateTimeOffset TakenAt { get; set; }

    public Guid? OwnerId { get; set; }

    public Guid? LinkedAccountId { get; set; }

    /// <summary>Text, not JSON: the database would reorder a JSON document's keys.</summary>
    public string Payload { get; set; } = "";
}

/// <summary>A store entry a linked account owns, with the playtime of the latest snapshot that listed
/// it.</summary>
public sealed class OwnershipRow
{
    public Guid LinkedAccountId { get; set; }

    public Guid StoreEntryId { get; set; }

    public long PlaytimeSeconds { get; set; }

    public DateTimeOffset? LastPlayedAt { get; set; }

    /// <summary>When a snapshot first listed it.</summary>
    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>The snapshot the playtime comes from.</summary>
    public Guid SnapshotId { get; set; }
}
