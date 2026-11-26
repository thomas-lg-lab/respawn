using System.Net;
using System.Text;

namespace Respawn.Games.Steam;

/// <summary>
/// How the connector asks Steam anything: the key (the one handed over, or the instance's), in a
/// header, for what needs one and for nothing else; the time Steam has to answer; the size and the
/// encoding of the answer; and what a refusal means. Read-only: nothing is ever written to Steam.
/// </summary>
internal sealed class SteamClient(IHttpClientFactory http, Func<SteamOptions> options, TimeProvider clock)
{
    /// <summary>Steam answers in UTF-8, whatever the answer's headers say; bytes that are not UTF-8 are
    /// refused rather than replaced.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Checks a SteamID as Steam writes it: the 17 digits of its 64-bit form. Nothing else goes
    /// in an address.</summary>
    public static void CheckSteamId(string externalId)
    {
        if (externalId is not { Length: 17 } || !externalId.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("a Steam account is named by its 17-digit SteamID", nameof(externalId));
        }
    }

    /// <summary>Asks Steam, and returns its answer as text with the moment it answered.</summary>
    /// <param name="address">The method and its parameters, relative to <see cref="SteamLibrarySource.Api"/>;
    /// never the key.</param>
    /// <param name="credential">The owner's own key, or null for the instance's.</param>
    /// <param name="ct">Gives up.</param>
    /// <exception cref="SourceException">Steam could not be read.</exception>
    public async Task<(string Body, DateTimeOffset TakenAt)> GetAsync(string address, string? credential, CancellationToken ct)
    {
        // The owner's own key when one is handed over, the instance's otherwise.
        var key = credential?.Trim()
            ?? options().Resolve()
            ?? throw new SourceException(SourceError.NotConfigured, "the instance has no Steam Web API key");
        if (!SteamOptions.IsKey(key))
        {
            // Never sent anywhere, and never quoted.
            throw new SourceException(
                SourceError.NotConfigured,
                credential is null
                    ? "what the instance was given as its Steam Web API key is not one: a key is letters and digits"
                    : "what was given as a Steam Web API key is not one: a key is letters and digits");
        }

        var answer = await SendAsync(SteamLibrarySource.HttpClientName, address, key, SteamLibrarySource.MaxAnswerBytes, SteamLibrarySource.Patience, ct);
        if (answer.Status != HttpStatusCode.OK)
        {
            throw Refusal(answer.Status, credential is null ? "the instance's key" : "the key given on this account");
        }

        return (Text(answer), answer.TakenAt);
    }

    /// <summary>Asks Steam what it says to anyone, and returns its answer as text: no key is sent,
    /// neither the instance's nor an owner's.</summary>
    /// <param name="address">The method and its parameters, relative to <see cref="SteamLibrarySource.Api"/>.</param>
    /// <param name="ct">Gives up.</param>
    /// <exception cref="SourceException">Steam could not be read.</exception>
    public async Task<string> GetOpenAsync(string address, CancellationToken ct)
    {
        var answer = await SendAsync(SteamCoverSource.StoreClientName, address, null, SteamLibrarySource.MaxAnswerBytes, SteamLibrarySource.Patience, ct);
        if (answer.Status != HttpStatusCode.OK)
        {
            throw Refusal(answer.Status, "the request");
        }

        return Text(answer);
    }

    /// <summary>Asks Steam's image host for a file, with no key. Whatever it answers is handed over:
    /// what an answer other than a file means is the caller's to say. The bytes are there when Steam
    /// answered 200 with no more than <see cref="SteamCoverSource.MaxImageBytes"/>.</summary>
    /// <param name="address">The file, relative to <see cref="SteamCoverSource.Images"/>.</param>
    /// <param name="ct">Gives up.</param>
    /// <exception cref="SourceException">Steam did not answer.</exception>
    public async Task<(HttpStatusCode Status, byte[]? Bytes)> GetImageAsync(string address, CancellationToken ct)
    {
        var answer = await SendAsync(SteamCoverSource.ImagesClientName, address, null, SteamCoverSource.MaxImageBytes, SteamCoverSource.Patience, ct);
        return (answer.Status, answer.Bytes);
    }

    /// <summary>Steam's answer as text.</summary>
    private static string Text(Answer answer)
    {
        if (answer.Bytes is null)
        {
            throw new SourceException(SourceError.Unreadable, $"Steam's answer is larger than {SteamLibrarySource.MaxAnswerBytes / (1024 * 1024)} MB");
        }

        try
        {
            return Utf8.GetString(answer.Bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new SourceException(SourceError.Unreadable, "Steam's answer is not text", ex);
        }
    }

    /// <summary>One question to one of Steam's hosts: the key in a header when there is one, the time
    /// Steam has to answer, and the answer's bytes when it is a 200 of an accepted size.</summary>
    private async Task<Answer> SendAsync(string clientName, string address, string? key, int maxBytes, TimeSpan patience, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        if (key is not null)
        {
            request.Headers.Add(SteamLibrarySource.KeyHeader, key);
        }

        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waiting.CancelAfter(patience);
        try
        {
            using var client = http.CreateClient(clientName);
            using var answer = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, waiting.Token);
            var takenAt = clock.GetUtcNow();
            return answer.StatusCode == HttpStatusCode.OK
                ? new Answer(answer.StatusCode, await BodyAsync(answer, maxBytes, waiting.Token), takenAt)
                : new Answer(answer.StatusCode, null, takenAt);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
            && ex is HttpRequestException or IOException or OperationCanceledException)
        {
            // No connection, an answer cut short, or Steam taking too long. A caller that gave up is
            // not a failure of Steam: its cancellation goes through as it is.
            throw new SourceException(SourceError.Unavailable, "Steam did not answer", ex);
        }
    }

    /// <summary>The bytes of an answer, read here rather than by the client: the size is counted on
    /// the way, and the charset its headers name is never trusted. Null when it is larger than
    /// allowed.</summary>
    private static async Task<byte[]?> BodyAsync(HttpResponseMessage answer, int maxBytes, CancellationToken ct)
    {
        if (answer.Content.Headers.ContentLength > maxBytes)
        {
            return null;
        }

        await using var stream = await answer.Content.ReadAsStreamAsync(ct);
        using var kept = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (kept.Length + read > maxBytes)
            {
                return null;
            }

            kept.Write(buffer, 0, read);
        }

        return kept.ToArray();
    }

    /// <summary>What a host of Steam answered: the bytes when it is a 200 of an accepted size.</summary>
    private sealed record Answer(HttpStatusCode Status, byte[]? Bytes, DateTimeOffset TakenAt);

    /// <summary>What an answer other than 200 means.</summary>
    /// <param name="status">Steam's answer.</param>
    /// <param name="refused">What Steam refused when it says so: a key, or the request itself.</param>
    private static SourceException Refusal(HttpStatusCode status, string refused)
    {
        var code = (int)status;
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new SourceException(SourceError.Refused, $"Steam refused {refused} ({code})"),
            HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError =>
                new SourceException(SourceError.Unavailable, $"Steam asked to come back later ({code})"),
            _ => new SourceException(SourceError.Unreadable, $"Steam answered {code}"),
        };
    }
}
