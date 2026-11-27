using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Respawn.Games.Igdb;

/// <summary>
/// IGDB's image host: a cover by the <c>image_id</c> IGDB gives it, in the size the library shows
/// (<c>cover_big_2x</c>, 528 x 748). The images are public: no token, no client id is sent.
///
/// Nothing IGDB says is passed on as it is. An address is built from an image id of a known shape, on
/// one host; an image is one by its first bytes, whatever its headers say. A place the host says has
/// nothing (404) is no cover; any other answer is a host that could not be read.
/// </summary>
internal sealed partial class IgdbImages(IHttpClientFactory http)
{
    /// <summary>The client that asks the image host: it never carries a token.</summary>
    public const string ClientName = "igdb-images";

    /// <summary>Where the images are, by size.</summary>
    public static readonly Uri Host = new("https://images.igdb.com/igdb/image/upload/");

    /// <summary>The size a cover is asked in: twice the 264 x 374 of <c>cover_big</c>.</summary>
    public const string CoverSize = "t_cover_big_2x";

    /// <summary>The most an image may weigh: a cover is some tens of kilobytes.</summary>
    public const int MaxImageBytes = 5 * 1024 * 1024;

    /// <summary>The most images asked at once, and so the most connections kept to the host.</summary>
    public const int MaxConnections = 16;

    /// <summary>How long the host has to answer, body included.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>The turns at the host. Never disposed: the source lives as long as the app.</summary>
    private readonly SemaphoreSlim _turns = new(MaxConnections, MaxConnections);

    /// <summary>The cover of this id, or null when the host has none.</summary>
    /// <exception cref="SourceException">The host could not be read.</exception>
    public async Task<Cover?> ReadCoverAsync(string imageId, CancellationToken ct)
    {
        if (!IdShape.IsMatch(imageId))
        {
            return null;
        }

        await _turns.WaitAsync(ct);
        try
        {
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
            waiting.CancelAfter(Patience);
            try
            {
                using var client = http.CreateClient(ClientName);
                using var answer = await client.GetAsync($"{CoverSize}/{imageId}.jpg", HttpCompletionOption.ResponseHeadersRead, waiting.Token);
                if (answer.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    return null;
                }

                if (answer.StatusCode != HttpStatusCode.OK)
                {
                    var status = answer.StatusCode;
                    throw new SourceException(
                        status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError
                            ? SourceError.Unavailable
                            : SourceError.Unreadable,
                        string.Create(CultureInfo.InvariantCulture, $"IGDB's image host answered {(int)status}"));
                }

                // What is there and is no image is no cover: too large, or bytes that are not an image
                // whatever their headers say.
                return await BodyAsync(answer, waiting.Token) is { } bytes && MediaType(bytes) is { } type
                    ? new Cover(type, bytes)
                    : null;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested
                && ex is HttpRequestException or IOException or OperationCanceledException)
            {
                throw new SourceException(SourceError.Unavailable, "IGDB's image host did not answer", ex);
            }
        }
        finally
        {
            _turns.Release();
        }
    }

    /// <summary>The bytes of an answer, counted on the way; null when it is larger than allowed.</summary>
    private static async Task<byte[]?> BodyAsync(HttpResponseMessage answer, CancellationToken ct)
    {
        if (answer.Content.Headers.ContentLength > MaxImageBytes)
        {
            return null;
        }

        await using var stream = await answer.Content.ReadAsStreamAsync(ct);
        using var kept = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (kept.Length + read > MaxImageBytes)
            {
                return null;
            }

            kept.Write(buffer, 0, read);
        }

        return kept.ToArray();
    }

    /// <summary>The media type of an image by its first bytes: JPEG, PNG or WebP; null for anything else.</summary>
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

    /// <summary>IGDB's image ids: letters and digits, such as <c>co1wyy</c>.</summary>
    [GeneratedRegex(@"\A[A-Za-z0-9]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdShape { get; }
}
