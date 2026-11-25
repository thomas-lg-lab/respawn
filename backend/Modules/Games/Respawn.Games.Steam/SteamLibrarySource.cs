using System.Globalization;
using System.Text.Json;
using Respawn.Games.Ports;

namespace Respawn.Games.Steam;

/// <summary>
/// A Steam library, read through the official Steam Web API (<c>IPlayerService/GetOwnedGames</c>) with
/// a Steam Web API key: the one handed over with the request, or the instance's. Read-only: nothing is
/// ever written to Steam.
///
/// The answer is handed over untouched, beside what it says of each game. Steam counts playtime in
/// minutes and dates in seconds since 1970; a field it leaves out is not said, and stays null.
/// </summary>
internal sealed class SteamLibrarySource(SteamClient steam) : ILibrarySource
{
    public const string HttpClientName = "steam";

    /// <summary>Which of Steam's answers a reading is: the name its snapshots are kept under.</summary>
    public const string Kind = "owned-games";

    /// <summary>The most an answer may weigh. A library of a few thousand games is under one megabyte.</summary>
    public const int MaxAnswerBytes = 16 * 1024 * 1024;

    /// <summary>The header Steam reads the key from, in place of the <c>key</c> parameter: an address
    /// is written in logs, here and on the way, and a header is not.</summary>
    public const string KeyHeader = "x-webapi-key";

    public static readonly Uri Api = new("https://api.steampowered.com/");

    /// <summary>How long Steam has to answer, body included.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    public string Source => "steam";

    /// <summary>Steam's <c>playtime_2weeks</c>: the last two weeks.</summary>
    public TimeSpan RecentWindow => TimeSpan.FromDays(14);

    public bool IsCredential(string credential) => SteamOptions.IsKey(credential);

    public async Task<LibraryReading> ReadLibraryAsync(string externalId, string? credential, CancellationToken ct)
    {
        SteamClient.CheckSteamId(externalId);

        // Everything the account owns: the games' names, the free games it played or added to its
        // library, and the games Steam leaves out of its own lists by default.
        var (body, takenAt) = await steam.GetAsync(
            $"IPlayerService/GetOwnedGames/v1/?steamid={externalId}&include_appinfo=true&include_played_free_games=true"
                + "&include_free_sub=true&skip_unvetted_apps=false&format=json",
            credential,
            ct);

        var (items, complete) = Games(body);
        return new LibraryReading(Kind, takenAt, body, items, complete);
    }

    /// <summary>The games an answer lists, and whether it lists them all. A profile whose game details
    /// are private answers an empty <c>response</c>: no game, and nothing vouched for.</summary>
    private static (IReadOnlyList<OwnedItem> Items, bool Complete) Games(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("response", out var response)
                || response.ValueKind != JsonValueKind.Object)
            {
                throw new SourceException(SourceError.Unreadable, "Steam's answer has no response");
            }

            if (!response.TryGetProperty("games", out var games))
            {
                return ([], false);
            }

            var items = new List<OwnedItem>(games.GetArrayLength());
            foreach (var game in games.EnumerateArray())
            {
                items.Add(new OwnedItem(
                    game.GetProperty("appid").GetUInt32().ToString(CultureInfo.InvariantCulture),
                    game.TryGetProperty("name", out var name) ? name.GetString() : null,
                    Minutes(game, "playtime_forever"),
                    game.TryGetProperty("rtime_last_played", out var last) && last.GetInt64() > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(last.GetInt64())
                        : null,
                    Minutes(game, "playtime_2weeks")));
            }

            // Steam's own count agrees with what it listed: nothing was lost on the way. That Steam
            // itself listed everything it knows cannot be checked from here; this is the most a
            // connector can say, and the core decides what a game left out becomes.
            var complete = response.TryGetProperty("game_count", out var count)
                && count.TryGetInt32(out var said)
                && said == items.Count;
            return (items, complete);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or ArgumentOutOfRangeException or OverflowException)
        {
            throw new SourceException(SourceError.Unreadable, "Steam's answer could not be read", ex);
        }
    }

    /// <summary>A playtime, when Steam gives one. A negative figure is no playtime: it is taken as not
    /// said, so that one game Steam counts wrong does not cost the whole library.</summary>
    private static TimeSpan? Minutes(JsonElement game, string field) =>
        game.TryGetProperty(field, out var said) && said.GetInt64() is >= 0 and var minutes
            ? TimeSpan.FromMinutes(minutes)
            : null;
}
