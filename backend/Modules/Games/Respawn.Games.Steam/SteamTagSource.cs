using System.Globalization;
using System.Text;
using System.Text.Json;
using Respawn.Games.Ports;

namespace Respawn.Games.Steam;

/// <summary>
/// The tags Steam's players gave its games, as the store shows them on a game's page: each game's twenty
/// heaviest, with their weights (<c>IStoreBrowseService/GetItems</c> with <c>include_tag_count</c>), and
/// their names in the app's languages, English alone today (<c>IStoreService/GetTagList</c>). Neither takes a key, and
/// none is sent: the store's tags are public. Neither is in Valve's documentation; the store's own pages
/// ask them, and the Steam Web API's terms apply.
///
/// Nothing Steam says is kept as it is: a tag's id is a number, a weight one too, a name a text of a
/// bounded size; anything else is left out.
/// </summary>
internal sealed class SteamTagSource(SteamClient steam, TimeProvider clock) : IStoreTagSource
{
    /// <summary>The most tags the store gives a game, as its page shows them.</summary>
    public const int TagsPerGame = 20;

    /// <summary>The app's languages, as Steam names them.</summary>
    private static readonly IReadOnlyDictionary<string, string> SteamLanguages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["en"] = "english",
    };

    public string Source => "steam";

    /// <summary>What <c>GetItems</c> takes in one request.</summary>
    public int GamesPerRequest => 50;

    public async Task<StoreTagsReading> ReadTagsAsync(IReadOnlyCollection<string> externalIds, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(externalIds.Count, GamesPerRequest);
        string[] ids = [.. externalIds.Where(StoreIds.IsSteamAppId).Distinct(StringComparer.Ordinal)];

        // What is no app id is no game of the store: said to have no tag, without asking.
        var tags = externalIds.Where(id => !StoreIds.IsSteamAppId(id)).Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, _ => (IReadOnlyList<StoreTag>)[], StringComparer.Ordinal);
        if (ids.Length == 0)
        {
            return new StoreTagsReading([], tags);
        }

        // The tags only. The language and the country are part of the question and change nothing of a
        // tag's id or its weight.
        var asked = new StringBuilder("{\"ids\":[")
            .AppendJoin(',', ids.Select(id => $"{{\"appid\":{id}}}"))
            .Append(CultureInfo.InvariantCulture, $"],\"context\":{{\"language\":\"english\",\"country_code\":\"US\"}},\"data_request\":{{\"include_tag_count\":{TagsPerGame}}}}}")
            .ToString();
        var takenAt = clock.GetUtcNow();
        var body = await steam.GetOpenAsync("IStoreBrowseService/GetItems/v1/?input_json=" + Uri.EscapeDataString(asked), ct);
        foreach (var (id, said) in Tags(body, ids))
        {
            tags[id] = said;
        }

        return new StoreTagsReading([new SourceAnswer("store-tags", takenAt, body)], tags);
    }

    public async Task<StoreTagNamesReading> ReadTagNamesAsync(CancellationToken ct)
    {
        var answers = new List<SourceAnswer>();
        var names = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var language in StoreTagSchedule.Languages)
        {
            if (!SteamLanguages.TryGetValue(language, out var steamLanguage))
            {
                continue;
            }

            var takenAt = clock.GetUtcNow();
            var body = await steam.GetOpenAsync("IStoreService/GetTagList/v1/?language=" + steamLanguage, ct);
            answers.Add(new SourceAnswer("tag-names-" + language, takenAt, body));
            foreach (var (id, name) in Names(body))
            {
                if (!names.TryGetValue(id, out var byLanguage))
                {
                    names[id] = byLanguage = new Dictionary<string, string>(StringComparer.Ordinal);
                }

                byLanguage[language] = name;
            }
        }

        return new StoreTagNamesReading(answers, [.. names.Select(name => new StoreTagName(name.Key, name.Value))]);
    }

    /// <summary>What an answer says of the games' tags, for the games asked about and no other, the
    /// heaviest first: none for a game it names that the store does not know or that has no tag. A game
    /// the answer does not name is not in what is returned.</summary>
    private static Dictionary<string, IReadOnlyList<StoreTag>> Tags(string body, string[] asked)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var response = Response(document);
            var tags = new Dictionary<string, IReadOnlyList<StoreTag>>(StringComparer.Ordinal);
            // An answer about games always has their list, empty or not: without it, it says nothing.
            if (!response.TryGetProperty("store_items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                throw new SourceException(SourceError.Unreadable, "Steam's answer has no store_items");
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("id", out var id)
                    || id.ValueKind != JsonValueKind.Number
                    || !id.TryGetUInt32(out var number)
                    || number.ToString(CultureInfo.InvariantCulture) is not { } appId
                    || !asked.Contains(appId, StringComparer.Ordinal))
                {
                    continue;
                }

                var said = new List<StoreTag>();
                if (item.TryGetProperty("success", out var success)
                    && success.ValueKind == JsonValueKind.Number
                    && success.TryGetInt32(out var code) && code == 1
                    && item.TryGetProperty("tags", out var list)
                    && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tag in list.EnumerateArray())
                    {
                        if (tag.ValueKind == JsonValueKind.Object
                            && tag.TryGetProperty("tagid", out var tagId)
                            && tagId.ValueKind == JsonValueKind.Number
                            && tagId.TryGetUInt32(out var tagNumber)
                            && tag.TryGetProperty("weight", out var weight)
                            && weight.ValueKind == JsonValueKind.Number
                            && weight.TryGetInt32(out var weightNumber)
                            && weightNumber >= 0)
                        {
                            said.Add(new StoreTag(tagNumber.ToString(CultureInfo.InvariantCulture), weightNumber));
                        }
                    }
                }

                tags[appId] =
                [
                    .. said.DistinctBy(tag => tag.Id, StringComparer.Ordinal)
                        .OrderByDescending(tag => tag.Weight)
                        .Take(TagsPerGame),
                ];
            }

            return tags;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new SourceException(SourceError.Unreadable, "Steam's answer could not be read", ex);
        }
    }

    /// <summary>The tags an answer names, each with its name: those of a known shape alone.</summary>
    private static IEnumerable<(string Id, string Name)> Names(string body)
    {
        var names = new List<(string, string)>();
        try
        {
            using var document = JsonDocument.Parse(body);
            var response = Response(document);
            if (!response.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
            {
                throw new SourceException(SourceError.Unreadable, "Steam's answer has no tags");
            }

            foreach (var tag in tags.EnumerateArray())
            {
                if (tag.ValueKind == JsonValueKind.Object
                    && tag.TryGetProperty("tagid", out var id)
                    && id.ValueKind == JsonValueKind.Number
                    && id.TryGetUInt32(out var number)
                    && tag.TryGetProperty("name", out var name)
                    && name.ValueKind == JsonValueKind.String
                    && name.GetString()?.Trim() is { Length: > 0 and <= Limits.StoreTagName } text)
                {
                    names.Add((number.ToString(CultureInfo.InvariantCulture), text));
                }
            }

            return names;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new SourceException(SourceError.Unreadable, "Steam's answer could not be read", ex);
        }
    }

    private static JsonElement Response(JsonDocument document) =>
        document.RootElement.ValueKind == JsonValueKind.Object
        && document.RootElement.TryGetProperty("response", out var response)
        && response.ValueKind == JsonValueKind.Object
            ? response
            : throw new SourceException(SourceError.Unreadable, "Steam's answer has no response");
}
