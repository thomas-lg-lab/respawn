namespace Respawn.Games.Infrastructure.Persistence;

// The rows of the games schema. An owner is an account of the instance: its id is kept, and nothing
// here points into the tables of the module that holds accounts.

/// <summary>A game, once, whatever the store: the canonical record. Its IGDB id when it is known; no
/// name while no source has given one.</summary>
public sealed class GameRow
{
    public Guid Id { get; set; }

    public string? Name { get; set; }

    /// <summary>True while the name is the one a store entry gave when the record was created: a
    /// reading may then replace it. Whatever names the game otherwise (a person, a merge, another
    /// source) sets it to false, and readings leave the name alone from then on.</summary>
    public bool NameFromStore { get; set; }

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

    /// <summary>When the source answered.</summary>
    public DateTimeOffset TakenAt { get; set; }

    /// <summary>When the answer was stored, which may be much later.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    public Guid? OwnerId { get; set; }

    public Guid? LinkedAccountId { get; set; }

    /// <summary>Text, not JSON: the database would reorder a JSON document's keys.</summary>
    public string Payload { get; set; } = "";
}

/// <summary>What one snapshot said of one store entry, as it said it: null where it said nothing. Never
/// changed afterwards; the history of a library is a query over these rows and their snapshots' dates.</summary>
public sealed class ObservationRow
{
    public Guid SnapshotId { get; set; }

    public Guid StoreEntryId { get; set; }

    public long? PlaytimeSeconds { get; set; }

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
}
