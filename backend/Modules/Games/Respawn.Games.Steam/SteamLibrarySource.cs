using System.Globalization;
using System.Net;
using System.Text.Json;
using Respawn.Games.Ports;

namespace Respawn.Games.Steam;

/// <summary>
/// A Steam library, read through the official Steam Web API (<c>IPlayerService/GetOwnedGames</c>) with
/// the instance's key. Read-only: nothing is ever written to Steam.
///
/// The answer is handed over untouched, beside what it says of each game. Steam counts playtime in
/// minutes and dates in seconds since 1970; a field it leaves out is not said, and stays null.
/// </summary>
internal sealed class SteamLibrarySource(IHttpClientFactory http, Func<SteamOptions> options, TimeProvider clock)
    : ILibrarySource
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

    public string Source => "steam";

    public async Task<LibraryReading> ReadLibraryAsync(string externalId, CancellationToken ct)
    {
        // A SteamID as Steam writes it: the 17 digits of its 64-bit form. Nothing else goes in the address.
        if (externalId is not { Length: 17 } || !externalId.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("a Steam account is named by its 17-digit SteamID", nameof(externalId));
        }

        var key = options().Resolve()
            ?? throw new SourceException(SourceError.NotConfigured, "the instance has no Steam Web API key");

        // Everything the account owns: the games' names, the free games it played or added to its
        // library, and the games Steam leaves out of its own lists by default.
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"IPlayerService/GetOwnedGames/v1/?steamid={externalId}&include_appinfo=true&include_played_free_games=true"
                + "&include_free_sub=true&skip_unvetted_apps=false&format=json");
        request.Headers.Add(KeyHeader, key);

        string body;
        DateTimeOffset takenAt;
        try
        {
            using var client = http.CreateClient(HttpClientName);
            using var answer = await client.SendAsync(request, ct);
            takenAt = clock.GetUtcNow();
            if (answer.StatusCode != HttpStatusCode.OK)
            {
                throw Refusal(answer.StatusCode);
            }

            body = await answer.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException
            || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // No connection, an answer cut short or too large, or the client's timeout.
            throw new SourceException(SourceError.Unavailable, "Steam did not answer", ex);
        }

        var (items, complete) = Games(body);
        return new LibraryReading(Kind, takenAt, body, items, complete);
    }

    private static SourceException Refusal(HttpStatusCode status)
    {
        var code = (int)status;
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new SourceException(SourceError.Refused, $"Steam refused the instance's key ({code})"),
            HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError =>
                new SourceException(SourceError.Unavailable, $"Steam asked to come back later ({code})"),
            _ => new SourceException(SourceError.Unreadable, $"Steam answered {code}"),
        };
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

            // The whole library when Steam's own count agrees with what it listed.
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

    private static TimeSpan? Minutes(JsonElement game, string field) =>
        game.TryGetProperty(field, out var minutes) ? TimeSpan.FromMinutes(minutes.GetInt64()) : null;
}
