namespace Respawn.Games;

/// <summary>One of the tags a store's players gave a game, with how much weight the store gives it.</summary>
/// <param name="Id">The tag's id in the store, the same in every language.</param>
/// <param name="Weight">How much the store weighs it on that game: only its order among the game's tags
/// means something.</param>
public sealed record StoreTag(string Id, int Weight);

/// <summary>What a store answered when asked for some of its games' tags: its answers untouched, and the
/// tags read from them.</summary>
/// <param name="Answers">Its answers as they came; none when nothing was worth asking.</param>
/// <param name="Tags">The tags of each game the store answered about, by the game's id in the store, the
/// heaviest first; none for a game it knows with no tag, or does not know. A game the answer says nothing
/// about is left out.</param>
public sealed record StoreTagsReading(IReadOnlyList<SourceAnswer> Answers, IReadOnlyDictionary<string, IReadOnlyList<StoreTag>> Tags);

/// <summary>A store's name for one of its tags, in each language it was asked in.</summary>
/// <param name="Id">The tag's id in the store.</param>
/// <param name="Names">Its name, by language (<see cref="StoreTagSchedule.Languages"/>).</param>
public sealed record StoreTagName(string Id, IReadOnlyDictionary<string, string> Names);

/// <summary>What a store answered when asked for the names of its tags.</summary>
public sealed record StoreTagNamesReading(IReadOnlyList<SourceAnswer> Answers, IReadOnlyList<StoreTagName> Names);

/// <summary>A store entry whose tags the store is to be asked for.</summary>
public sealed record StoreTagEntry(Guid StoreEntryId, string ExternalId);

/// <summary>The tags a store gave one of its entries, the heaviest first; none when it gave none.</summary>
/// <param name="StoreEntryId">The store entry.</param>
/// <param name="Tags">Null when the store's answer said nothing of the entry: the tags it had are kept, and
/// it is still asked again only once <see cref="StoreTagSchedule.AskAgainAfter"/> has passed.</param>
public sealed record StoreEntryTags(Guid StoreEntryId, IReadOnlyList<StoreTag>? Tags);

/// <summary>A tag as the library shows it: its key, unique across stores, and its names.</summary>
/// <param name="Key">The store and the tag's id in it, as <c>store:id</c> (<see cref="StoreTagKeys"/>).</param>
/// <param name="Names">Its name by language; a language the store gave no name in is left out.</param>
public sealed record NamedStoreTag(string Key, IReadOnlyDictionary<string, string> Names);

/// <summary>How asking a store for its games' tags ended.</summary>
/// <param name="Read">The store entries whose tags were kept by this run, none included.</param>
/// <param name="Error">Why it could not be read, when it could not: the entries left are asked about once
/// <see cref="StoreTagSchedule.RetryAfter"/> has passed.</param>
/// <param name="Detail">What happened, in the connector's words.</param>
public sealed record StoreTagsRead(int Read, SourceError? Error = null, string? Detail = null);

/// <summary>When the stores are asked for their games' tags: apart from the sync, which needs none of them,
/// once for the whole instance per store entry, whoever owns it.</summary>
public static class StoreTagSchedule
{
    /// <summary>How long a game's tags are taken before the store is asked again: players keep tagging.</summary>
    public static readonly TimeSpan AskAgainAfter = TimeSpan.FromDays(30);

    /// <summary>How long a store's names of its tags are taken before it is asked them again, at a turn
    /// that has games to ask about: a tag it adds shows by its key until then.</summary>
    public static readonly TimeSpan NamesAskAgainAfter = TimeSpan.FromDays(1);

    /// <summary>How long a store's games are left alone after it could not be read.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

    /// <summary>The most store entries asked about in one turn: a library read for the first time is
    /// spread over several turns.</summary>
    public const int EntriesPerTurn = 200;

    /// <summary>The languages a tag's name is asked in, as the app names them: English alone, whatever the
    /// language of the front, as the genres and themes IGDB gives are.</summary>
    public static readonly IReadOnlyList<string> Languages = ["en"];

    /// <summary>What is due at that moment: an entry asked before this, or never.</summary>
    public static DateTimeOffset DueBefore(DateTimeOffset now) => now - AskAgainAfter;
}

/// <summary>The key a tag goes by outside its store: the store, then the tag's id in it.</summary>
public static class StoreTagKeys
{
    public static string Of(string store, string tagId) => $"{store}:{tagId}";
}

/// <summary>The questions on their way, in this process, and when each store could last not be asked for
/// its games' tags: one at a time per store, and not again before <see cref="StoreTagSchedule.RetryAfter"/>.
/// A restart forgets it, and asks once more.</summary>
public sealed class StoreTagGate
{
    private readonly HashSet<string> _running = [];
    private readonly Dictionary<string, DateTimeOffset> _failed = [];

    public bool TryEnter(string store)
    {
        lock (_running)
        {
            return _running.Add(store);
        }
    }

    public void Leave(string store)
    {
        lock (_running)
        {
            _running.Remove(store);
        }
    }

    /// <summary>When the store could last not be read, while no turn went through since.</summary>
    public DateTimeOffset? FailedAt(string store)
    {
        lock (_running)
        {
            return _failed.TryGetValue(store, out var at) ? at : null;
        }
    }

    public void Failed(string store, DateTimeOffset at)
    {
        lock (_running)
        {
            _failed[store] = at;
        }
    }

    public void Succeeded(string store)
    {
        lock (_running)
        {
            _failed.Remove(store);
        }
    }
}
