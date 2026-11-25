using System.Net;
using System.Text;

namespace Respawn.Games.Steam;

/// <summary>
/// How the connector asks the Steam Web API anything: the key (the one handed over, or the instance's),
/// in a header; the time Steam has to answer; the size and the encoding of the answer; and what a
/// refusal means. Read-only: nothing is ever written to Steam.
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

        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Add(SteamLibrarySource.KeyHeader, key);

        byte[] bytes;
        DateTimeOffset takenAt;
        using var patience = CancellationTokenSource.CreateLinkedTokenSource(ct);
        patience.CancelAfter(SteamLibrarySource.Patience);
        try
        {
            using var client = http.CreateClient(SteamLibrarySource.HttpClientName);
            using var answer = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, patience.Token);
            takenAt = clock.GetUtcNow();
            if (answer.StatusCode != HttpStatusCode.OK)
            {
                throw Refusal(answer.StatusCode, own: credential is not null);
            }

            bytes = await BodyAsync(answer, patience.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
            && ex is HttpRequestException or IOException or OperationCanceledException)
        {
            // No connection, an answer cut short, or Steam taking too long. A caller that gave up is
            // not a failure of Steam: its cancellation goes through as it is.
            throw new SourceException(SourceError.Unavailable, "Steam did not answer", ex);
        }

        try
        {
            return (Utf8.GetString(bytes), takenAt);
        }
        catch (DecoderFallbackException ex)
        {
            throw new SourceException(SourceError.Unreadable, "Steam's answer is not text", ex);
        }
    }

    /// <summary>The bytes of an answer, read here rather than by the client: an answer too large is
    /// refused as unreadable, and the charset its headers name is never trusted.</summary>
    private static async Task<byte[]> BodyAsync(HttpResponseMessage answer, CancellationToken ct)
    {
        if (answer.Content.Headers.ContentLength > SteamLibrarySource.MaxAnswerBytes)
        {
            throw TooLarge();
        }

        await using var stream = await answer.Content.ReadAsStreamAsync(ct);
        using var kept = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (kept.Length + read > SteamLibrarySource.MaxAnswerBytes)
            {
                throw TooLarge();
            }

            kept.Write(buffer, 0, read);
        }

        return kept.ToArray();

        static SourceException TooLarge() =>
            new(SourceError.Unreadable, $"Steam's answer is larger than {SteamLibrarySource.MaxAnswerBytes / (1024 * 1024)} MB");
    }

    private static SourceException Refusal(HttpStatusCode status, bool own)
    {
        var code = (int)status;
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new SourceException(
                    SourceError.Refused,
                    own ? $"Steam refused the key given on this account ({code})" : $"Steam refused the instance's key ({code})"),
            HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError =>
                new SourceException(SourceError.Unavailable, $"Steam asked to come back later ({code})"),
            _ => new SourceException(SourceError.Unreadable, $"Steam answered {code}"),
        };
    }
}
