using System.Globalization;
using System.Text;
using System.Text.Json;
using Respawn.Games.Ports;

namespace Respawn.Games.Igdb;

/// <summary>
/// Which IGDB games the games of a store are, read through <c>external_games</c>: a row per game of a
/// store IGDB knows, its <c>uid</c> the id in the store, its <c>external_game_source</c> the store (1 for
/// Steam), its <c>game</c> the id of IGDB's game. A store's game may be several of IGDB's, or none.
///
/// The answers are handed over untouched, beside the matches read from them. IGDB gives 500 rows at most
/// an answer: a list that may hold more is read page after page.
/// </summary>
internal sealed class IgdbMatchSource(IgdbClient igdb, TimeProvider clock) : IGameDataSource
{
    /// <summary>Which of IGDB's answers a reading is: the name its snapshots are kept under.</summary>
    public const string Kind = "external-games";

    /// <summary>The most rows IGDB gives in one answer.</summary>
    public const int RowsPerAnswer = 500;

    /// <summary>The most pages read for one question: a store's game is rarely more than two of IGDB's,
    /// so 500 ids never fill ten pages. More is an answer that does not end.</summary>
    private const int MaxPages = 10;

    /// <summary>IGDB's number for each store it is asked about, by the name store entries give it.</summary>
    private static readonly Dictionary<string, int> Stores = new(StringComparer.Ordinal) { ["steam"] = 1 };

    public string Source => "igdb";

    public int MatchesPerRequest => 500;

    public async Task<MatchReading> MatchAsync(string store, IReadOnlyCollection<string> externalIds, CancellationToken ct)
    {
        if (!Stores.TryGetValue(store, out var number))
        {
            throw new ArgumentException($"IGDB is not asked about the games of {store}", nameof(store));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(externalIds.Count, MatchesPerRequest, nameof(externalIds));

        // Nothing that cannot be a game of the store goes in a question: Steam's app ids are whole numbers.
        var asked = externalIds.Where(IsAppId).ToHashSet(StringComparer.Ordinal);
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

    /// <summary>A Steam app id: a 32-bit whole number, written plainly.</summary>
    private static bool IsAppId(string id) =>
        id.Length is > 0 and <= 10 && id.All(char.IsAsciiDigit)
        && uint.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    /// <summary>Adds the matches of one answer, and says how many rows it had.</summary>
    private static int Read(string body, HashSet<string> asked, List<GameMatch> matches)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new SourceException(SourceError.Unreadable, "IGDB's answer is not a list");
            }

            foreach (var row in document.RootElement.EnumerateArray())
            {
                // A row with no game, or about a game that was not asked, says nothing of the games asked.
                if (row.ValueKind == JsonValueKind.Object
                    && row.TryGetProperty("uid", out var uid) && uid.ValueKind == JsonValueKind.String
                    && uid.GetString() is { } externalId && asked.Contains(externalId)
                    && row.TryGetProperty("game", out var game) && game.TryGetInt64(out var gameId) && gameId > 0)
                {
                    matches.Add(new GameMatch(externalId, gameId.ToString(CultureInfo.InvariantCulture)));
                }
            }

            return document.RootElement.GetArrayLength();
        }
        catch (JsonException ex)
        {
            throw new SourceException(SourceError.Unreadable, "IGDB's answer could not be read", ex);
        }
    }
}
