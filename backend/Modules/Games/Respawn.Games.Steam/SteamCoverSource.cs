using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Respawn.Games.Ports;

namespace Respawn.Games.Steam;

/// <summary>
/// A Steam game's cover, the portrait image of the Steam library, from Steam's image host; and where
/// Steam's store says that image is (<c>IStoreBrowseService/GetItems</c> with its assets). Neither
/// needs a key, and none is sent: a cover is public.
///
/// An image is looked for where the store said, when it was asked, then at the two addresses every
/// game had before the store gave each image a folder of its own: <c>library_600x900.jpg</c>, and
/// <c>portrait.png</c>, the form of the oldest games. Where the store said comes first: for a game
/// whose artwork moved, the old address may still answer, with a blank image.
///
/// Nothing Steam says is passed on as it is. An address is built from an app id made of digits and a
/// path of a known shape, on one host; an image is one by its first bytes, whatever its headers say.
/// </summary>
internal sealed partial class SteamCoverSource(SteamClient steam) : ICoverSource
{
    /// <summary>The client that asks Steam's store what anyone may ask: it never carries a key.</summary>
    public const string StoreClientName = "steam-store";

    /// <summary>The client that asks Steam's image host: it never carries a key.</summary>
    public const string ImagesClientName = "steam-images";

    /// <summary>The most an image may weigh. A cover is some tens of kilobytes, and the oldest, in
    /// PNG, a few hundred.</summary>
    public const int MaxImageBytes = 5 * 1024 * 1024;

    /// <summary>The folder of every app's images on Steam's image host.</summary>
    public static readonly Uri Images = new("https://shared.steamstatic.com/store_item_assets/steam/apps/");

    /// <summary>How long the image host has to answer, body included.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>Where every game's cover was before the store gave each image a folder of its own.</summary>
    private static readonly string[] FixedPaths = ["library_600x900.jpg", "portrait.png"];

    public string Source => "steam";

    /// <summary>What <c>GetItems</c> takes in one request.</summary>
    public int PathsPerRequest => 50;

    public async Task<Cover?> ReadCoverAsync(string externalId, string? path, CancellationToken ct)
    {
        if (!IsAppId(externalId))
        {
            return null;
        }

        string[] places = path is not null && IsPath(path) ? [path, .. FixedPaths.Where(place => place != path)] : FixedPaths;
        foreach (var place in places)
        {
            var (status, bytes) = await steam.GetImageAsync($"{externalId}/{place}", ct);
            if (status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError)
            {
                throw new SourceException(
                    SourceError.Unavailable,
                    string.Create(CultureInfo.InvariantCulture, $"Steam's image host asked to come back later ({(int)status})"));
            }

            // Anything else than an image is no cover at that place: not found, sent elsewhere, too
            // large, or bytes that are not an image whatever their headers say.
            if (bytes is not null && MediaType(bytes) is { } type)
            {
                return new Cover(type, bytes);
            }
        }

        return null;
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadCoverPathsAsync(IReadOnlyCollection<string> externalIds, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(externalIds.Count, PathsPerRequest);
        string[] ids = [.. externalIds.Where(IsAppId).Distinct(StringComparer.Ordinal)];
        if (ids.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        // The assets only: prices, tags and the rest are not asked for. The language and the country
        // are part of the question and change nothing of a path.
        var asked = new StringBuilder("{\"ids\":[")
            .AppendJoin(',', ids.Select(id => $"{{\"appid\":{id}}}"))
            .Append("],\"context\":{\"language\":\"english\",\"country_code\":\"US\"},\"data_request\":{\"include_assets\":true}}")
            .ToString();
        var body = await steam.GetOpenAsync("IStoreBrowseService/GetItems/v1/?input_json=" + Uri.EscapeDataString(asked), ct);
        return Paths(body, ids);
    }

    /// <summary>The covers' places an answer gives, for the games asked about and no other. A game the
    /// store does not know answers without success and without assets.</summary>
    private static Dictionary<string, string> Paths(string body, string[] asked)
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

            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!response.TryGetProperty("store_items", out var items))
            {
                return paths;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("success", out var success) && success.GetInt32() == 1
                    && item.TryGetProperty("id", out var id)
                    && id.GetUInt32().ToString(CultureInfo.InvariantCulture) is var appId
                    && asked.Contains(appId, StringComparer.Ordinal)
                    && item.TryGetProperty("assets", out var assets)
                    && assets.ValueKind == JsonValueKind.Object
                    && assets.TryGetProperty("library_capsule", out var capsule)
                    && capsule.ValueKind == JsonValueKind.String
                    && capsule.GetString() is { } path
                    && IsPath(path))
                {
                    paths[appId] = path;
                }
            }

            return paths;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new SourceException(SourceError.Unreadable, "Steam's answer could not be read", ex);
        }
    }

    /// <summary>Whether this is a Steam app id: digits, and nothing else goes in an address.</summary>
    private static bool IsAppId(string externalId) =>
        externalId.Length is > 0 and <= 10 && externalId.All(char.IsAsciiDigit);

    /// <summary>Whether this can be where a cover is, under an app's folder: an image file's name, in
    /// one folder at most, of letters, digits, dashes and underscores. Nothing else is kept, and
    /// nothing else goes in an address: no dot but the one before the extension, so no way up.</summary>
    private static bool IsPath(string path) => path.Length <= Limits.CoverPath && PathShape.IsMatch(path);

    /// <summary>What kind of image these bytes start as, among those passed on; null for anything
    /// else.</summary>
    private static string? MediaType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith<byte>([0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (bytes.StartsWith<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        return bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)
            ? "image/webp"
            : null;
    }

    [GeneratedRegex(@"\A(?:[A-Za-z0-9_-]{1,64}/)?[A-Za-z0-9_-]{1,64}\.(?:jpg|jpeg|png|webp)\z", RegexOptions.CultureInvariant)]
    private static partial Regex PathShape { get; }
}
