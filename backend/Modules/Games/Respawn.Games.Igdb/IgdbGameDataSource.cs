using System.Globalization;
using System.Text.Json;
using Respawn.Games.Ports;

namespace Respawn.Games.Igdb;

/// <summary>
/// What IGDB knows of the games of the stores.
///
/// Which IGDB games the games of a store are, read through <c>external_games</c>: a row per game of a
/// store IGDB knows, its <c>uid</c> the id in the store, its <c>external_game_source</c> the store (1 for
/// Steam), its <c>game</c> the id of IGDB's game. A store's game may be several of IGDB's, or none.
///
/// What IGDB says of its own games, read through <c>games</c> (the name, the page, the cover, the genres,
/// the themes, the summary) and <c>game_time_to_beats</c> (how long players took).
///
/// The answers are handed over untouched, beside what was read from them. IGDB gives 500 rows at most an
/// answer: a list that may hold more is read page after page.
/// </summary>
internal sealed class IgdbGameDataSource(IgdbClient igdb, IgdbImages images, TimeProvider clock) : IGameDataSource
{
    /// <summary>Which of IGDB's answers a reading is: the name its snapshots are kept under.</summary>
    public const string Kind = "external-games";

    /// <summary>The answer about IGDB's games.</summary>
    public const string GamesKind = "games";

    /// <summary>The answer about how long they take.</summary>
    public const string TimeToBeatsKind = "game-time-to-beats";

    /// <summary>The most rows IGDB gives in one answer.</summary>
    public const int RowsPerAnswer = 500;

    /// <summary>The most pages read for one question: a store's game is rarely more than two of IGDB's,
    /// so 500 ids never fill ten pages. More is an answer that does not end.</summary>
    private const int MaxPages = 10;

    /// <summary>Where a game's page is: a link that leads anywhere else is not kept.</summary>
    private const string PagePrefix = "https://www.igdb.com/games/";

    /// <summary>IGDB's number for each store it is asked about, by the name store entries give it.</summary>
    private static readonly Dictionary<string, int> StoreNumbers = new(StringComparer.Ordinal) { ["steam"] = 1 };

    public string Source => "igdb";

    public IReadOnlyCollection<string> Stores => StoreNumbers.Keys;

    public bool HasAccess => igdb.HasAccess;

    public string Label => "IGDB";

    public Uri Home { get; } = new("https://www.igdb.com/");

    public Task<Cover?> ReadCoverAsync(string imageId, CancellationToken ct) => images.ReadCoverAsync(imageId, ct);

    public int MatchesPerRequest => 500;

    public int GamesPerRequest => RowsPerAnswer;

    public async Task<MatchReading> MatchAsync(string store, IReadOnlyCollection<string> externalIds, CancellationToken ct)
    {
        if (!StoreNumbers.TryGetValue(store, out var number))
        {
            throw new ArgumentException($"IGDB is not asked about the games of {store}", nameof(store));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(externalIds.Count, MatchesPerRequest, nameof(externalIds));

        // Nothing that cannot be a game of the store goes in a question: Steam's app ids are whole numbers.
        var asked = externalIds.Where(StoreIds.IsSteamAppId).ToHashSet(StringComparer.Ordinal);
        if (asked.Count == 0)
        {
            return new MatchReading(Kind, clock.GetUtcNow(), [], []);
        }

        var ids = string.Join(',', asked.Order(StringComparer.Ordinal).Select(id => $"\"{id}\""));
        var payloads = new List<string>();
        var matches = new List<GameMatch>();
        DateTimeOffset? takenAt = null;
        for (var page = 0; ; page++)
        {
            if (page == MaxPages)
            {
                throw new SourceException(SourceError.Unreadable, $"IGDB's answer runs past {MaxPages} pages");
            }

            var (body, at) = await igdb.PostAsync(
                "external_games",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"fields game,uid; where external_game_source = {number} & uid = ({ids}); sort id asc; limit {RowsPerAnswer}; offset {page * RowsPerAnswer};"),
                ct);
            takenAt ??= at;
            payloads.Add(body);
            var rows = Read(body, asked, matches);
            if (rows < RowsPerAnswer)
            {
                break;
            }
        }

        return new MatchReading(Kind, takenAt.Value, payloads, matches);
    }

    public async Task<GamesReading> ReadGamesAsync(IReadOnlyCollection<string> gameIds, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gameIds.Count, GamesPerRequest, nameof(gameIds));

        // Nothing that cannot be one of IGDB's games goes in a question: its ids are whole numbers.
        var asked = gameIds.Select(GameNumber).Where(id => id > 0).ToHashSet();
        if (asked.Count == 0)
        {
            return new GamesReading([], []);
        }

        var ids = string.Join(',', asked.Order().Select(id => id.ToString(CultureInfo.InvariantCulture)));
        var (games, gamesAt) = await igdb.PostAsync(
            "games",
            string.Create(
                CultureInfo.InvariantCulture,
                $"fields name,slug,url,cover.image_id,genres.name,themes.name,summary,game_type,version_parent,parent_game; where id = ({ids}); sort id asc; limit {RowsPerAnswer};"),
            ct);
        var (times, timesAt) = await igdb.PostAsync(
            "game_time_to_beats",
            string.Create(
                CultureInfo.InvariantCulture,
                $"fields game_id,hastily,normally,completely,count; where game_id = ({ids}); sort id asc; limit {RowsPerAnswer};"),
            ct);

        var durations = ReadTimes(times, asked);
        return new GamesReading(
            [new SourceAnswer(GamesKind, gamesAt, games), new SourceAnswer(TimeToBeatsKind, timesAt, times)],
            [.. ReadGames(games, asked).Select(game => game with { TimeToBeat = durations.GetValueOrDefault(game.GameId) })]);
    }

    /// <summary>An IGDB game's id: a whole number of at least one, written plainly; 0 for what is not.</summary>
    private static long GameNumber(string id) =>
        id.Length is > 0 and <= 18 && id.All(char.IsAsciiDigit)
        && long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : 0;

    /// <summary>Adds the matches of one answer, and says how many rows it had.</summary>
    private static int Read(string body, HashSet<string> asked, List<GameMatch> matches)
    {
        var rows = Rows(body);
        foreach (var row in rows)
        {
            // A row with no game, or about a game that was not asked, says nothing of the games asked.
            if (row.TryGetProperty("uid", out var uid) && uid.ValueKind == JsonValueKind.String
                && uid.GetString() is { } externalId && asked.Contains(externalId)
                && Id(row, "game") is { } gameId)
            {
                matches.Add(new GameMatch(externalId, gameId.ToString(CultureInfo.InvariantCulture)));
            }
        }

        return rows.Count;
    }

    /// <summary>The games of an answer of <c>games</c> that were asked about, each once.</summary>
    private static IEnumerable<GameFacts> ReadGames(string body, HashSet<long> asked)
    {
        var games = new Dictionary<long, GameFacts>();
        foreach (var row in Rows(body))
        {
            if (Id(row, "id") is not { } id || !asked.Contains(id) || games.ContainsKey(id))
            {
                continue;
            }

            var url = Text(row, "url", Limits.GameDataUrl);
            games[id] = new GameFacts(
                id.ToString(CultureInfo.InvariantCulture),
                Text(row, "name", Limits.Name),
                Word(row, "slug"),
                url is not null && url.StartsWith(PagePrefix, StringComparison.Ordinal) && Uri.IsWellFormedUriString(url, UriKind.Absolute) ? url : null,
                row.TryGetProperty("cover", out var cover) && cover.ValueKind == JsonValueKind.Object ? Word(cover, "image_id") : null,
                Names(row, "genres"),
                Names(row, "themes"),
                Text(row, "summary", Limits.GameDataSummary),
                null);
        }

        return games.Values;
    }

    /// <summary>How long each game asked about takes, by its id: the first row of a game that says
    /// anything, when there are several.</summary>
    private static Dictionary<string, TimeToBeat> ReadTimes(string body, HashSet<long> asked)
    {
        var times = new Dictionary<string, TimeToBeat>(StringComparer.Ordinal);
        foreach (var row in Rows(body))
        {
            if (Id(row, "game_id") is not { } id || !asked.Contains(id))
            {
                continue;
            }

            var time = new TimeToBeat(Whole(row, "hastily"), Whole(row, "normally"), Whole(row, "completely"), Whole(row, "count"));
            if (time is not { Hastily: null, Normally: null, Completely: null })
            {
                times.TryAdd(id.ToString(CultureInfo.InvariantCulture), time);
            }
        }

        return times;
    }

    /// <summary>The rows of an answer that is a list; those that are not objects are skipped.</summary>
    /// <exception cref="SourceException">The answer is not a list.</exception>
    private static List<JsonElement> Rows(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new SourceException(SourceError.Unreadable, "IGDB's answer is not a list");
            }

            return
            [
                .. document.RootElement.EnumerateArray()
                    .Where(row => row.ValueKind == JsonValueKind.Object)
                    .Select(row => row.Clone()),
            ];
        }
        catch (JsonException ex)
        {
            throw new SourceException(SourceError.Unreadable, "IGDB's answer could not be read", ex);
        }
    }

    /// <summary>An id of IGDB's: a whole number of at least one.</summary>
    private static long? Id(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var id) && id > 0
            ? id
            : null;

    /// <summary>A whole number of at least zero that fits an <see cref="int"/>: seconds, or players.</summary>
    private static int? Whole(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number) && number >= 0
            ? number
            : null;

    /// <summary>A text of at most <paramref name="max"/> characters, without its blanks around; null when
    /// there is none, or a longer one.</summary>
    private static string? Text(JsonElement row, string name, int max) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString()?.Trim() is { Length: > 0 } text && text.Length <= max
            ? text
            : null;

    private static string? Word(JsonElement row, string name) => Text(row, name, Limits.GameDataWord);

    /// <summary>The names of a list of objects, each once, in IGDB's order.</summary>
    private static List<string> Names(JsonElement row, string name) =>
        row.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ?
            [
                .. list.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object)
                    .Select(item => Word(item, "name"))
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal),
            ]
            : [];
}
