using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Respawn.Games.Igdb;

/// <summary>
/// How the connector asks IGDB anything: the token Twitch gives the instance for its client id and
/// secret, kept until it nears its end and asked again when IGDB stops taking it; IGDB's pace (four
/// requests a second, eight open at once); the time it has to answer; the size and the encoding of the
/// answer; and what a refusal means. Read-only: nothing is ever written to IGDB.
/// </summary>
internal sealed class IgdbClient(IHttpClientFactory http, Func<IgdbOptions> options, TimeProvider clock)
{
    /// <summary>IGDB's API: a question is a POST of its query language to an endpoint.</summary>
    public static readonly Uri Api = new("https://api.igdb.com/v4/");

    /// <summary>Twitch's sign-in, which gives the instance its token.</summary>
    public static readonly Uri Twitch = new("https://id.twitch.tv/oauth2/");

    /// <summary>The client that carries the token to IGDB.</summary>
    public const string HttpClientName = "igdb";

    /// <summary>The client that carries the client id and secret to Twitch, and nowhere else.</summary>
    public const string TokenClientName = "igdb-token";

    /// <summary>The most an answer may weigh. A page of 500 matches is about 40 KB.</summary>
    public const int MaxAnswerBytes = 4 * 1024 * 1024;

    /// <summary>How long IGDB or Twitch has to answer, the answer included.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>IGDB takes four requests a second: one starts at most every quarter of a second.</summary>
    public static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(250);

    /// <summary>IGDB takes eight requests open at once.</summary>
    public const int MaxOpen = 8;

    /// <summary>The token is asked again this long before its end, or a tenth of its life before when
    /// that is shorter. Twitch gives about 60 days.</summary>
    private static readonly TimeSpan Renewal = TimeSpan.FromDays(1);

    /// <summary>Twitch and IGDB answer in UTF-8; bytes that are not are refused rather than replaced.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly SemaphoreSlim _tokenTurn = new(1, 1);
    private readonly SemaphoreSlim _open = new(MaxOpen, MaxOpen);
    private readonly Lock _pace = new();
    private long _nextStart;
    private Token? _token;

    /// <summary>Asks IGDB one question, and returns its answer as text with the moment it answered.</summary>
    /// <param name="endpoint">The endpoint, relative to <see cref="Api"/>.</param>
    /// <param name="query">The question, in IGDB's query language.</param>
    /// <param name="ct">Gives up.</param>
    /// <exception cref="SourceException">IGDB could not be read.</exception>
    public async Task<(string Body, DateTimeOffset TakenAt)> PostAsync(string endpoint, string query, CancellationToken ct)
    {
        var access = options().Resolve()
            ?? throw new SourceException(SourceError.NotConfigured, "the instance has no IGDB access: no client id and secret");
        if (!IgdbOptions.IsCredential(access.ClientId) || !IgdbOptions.IsCredential(access.ClientSecret))
        {
            // Never sent anywhere, and never quoted.
            throw new SourceException(
                SourceError.NotConfigured, "what the instance was given as its IGDB client id or secret is not one: each is letters and digits");
        }

        var token = await TokenAsync(access, null, ct);
        var answer = await AskAsync(endpoint, query, access.ClientId, token.Value, ct);
        if (answer.Status == HttpStatusCode.Unauthorized)
        {
            // A token Twitch took back before its end, or one of another client: a new one, once.
            token = await TokenAsync(access, token, ct);
            answer = await AskAsync(endpoint, query, access.ClientId, token.Value, ct);
        }

        if (answer.Status != HttpStatusCode.OK)
        {
            throw Refusal("IGDB", answer.Status, "the instance's token");
        }

        return (Text(answer, "IGDB"), answer.TakenAt);
    }

    /// <summary>The token for this client id and secret: the one kept while it is far from its end, a
    /// new one from Twitch otherwise, or when IGDB refused the one kept.</summary>
    /// <param name="access">The instance's client id and secret, as they are now.</param>
    /// <param name="refused">The token IGDB just refused, which is not handed out again.</param>
    /// <param name="ct">Gives up.</param>
    private async Task<Token> TokenAsync((string ClientId, string ClientSecret) access, Token? refused, CancellationToken ct)
    {
        await _tokenTurn.WaitAsync(ct);
        try
        {
            if (_token is { } kept
                && kept.Access == access
                && kept != refused
                && clock.GetUtcNow() < kept.RenewAt)
            {
                return kept;
            }

            _token = null;
            var answer = await SendAsync(
                TokenClientName,
                "token",
                () => new FormUrlEncodedContent(
                [
                    new("client_id", access.ClientId),
                    new("client_secret", access.ClientSecret),
                    new("grant_type", "client_credentials"),
                ]),
                null,
                ct);
            if (answer.Status != HttpStatusCode.OK)
            {
                throw Refusal("Twitch", answer.Status, "the instance's IGDB client id and secret");
            }

            _token = ReadToken(Text(answer, "Twitch"), access, answer.TakenAt);
            return _token;
        }
        finally
        {
            _tokenTurn.Release();
        }
    }

    /// <summary>A token as Twitch gives it: <c>access_token</c>, and <c>expires_in</c> in seconds.</summary>
    private static Token ReadToken(string body, (string ClientId, string ClientSecret) access, DateTimeOffset takenAt)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("access_token", out var value) && value.ValueKind == JsonValueKind.String
                && value.GetString() is { } token && IgdbOptions.IsCredential(token)
                && root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt64(out var seconds) && seconds > 0)
            {
                var life = TimeSpan.FromSeconds(Math.Min(seconds, (long)TimeSpan.FromDays(3650).TotalSeconds));
                var margin = life / 10 < Renewal ? life / 10 : Renewal;
                return new Token(access, token, takenAt + life - margin);
            }
        }
        catch (JsonException ex)
        {
            throw new SourceException(SourceError.Unreadable, "Twitch's token could not be read", ex);
        }

        throw new SourceException(SourceError.Unreadable, "Twitch's answer holds no token");
    }

    /// <summary>One question to IGDB, at its pace: the client id and the token in their headers.</summary>
    private async Task<Answer> AskAsync(string endpoint, string query, string clientId, string token, CancellationToken ct)
    {
        await _open.WaitAsync(ct);
        try
        {
            await PaceAsync(ct);
            return await SendAsync(
                HttpClientName,
                endpoint,
                () => new StringContent(query, Utf8, "text/plain"),
                request =>
                {
                    request.Headers.Add("Client-ID", clientId);
                    request.Headers.Authorization = new("Bearer", token);
                },
                ct);
        }
        finally
        {
            _open.Release();
        }
    }

    /// <summary>Waits for this request's turn: one starts every <see cref="Spacing"/> at most, by the
    /// machine's own time, which is IGDB's measure, whatever the app's clock says.</summary>
    private async Task PaceAsync(CancellationToken ct)
    {
        TimeSpan wait;
        lock (_pace)
        {
            var now = Stopwatch.GetTimestamp();
            var start = Math.Max(now, _nextStart);
            _nextStart = start + (long)(Spacing.TotalSeconds * Stopwatch.Frequency);
            wait = Stopwatch.GetElapsedTime(now, start);
        }

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, ct);
        }
    }

    /// <summary>One POST, and the answer's bytes when it is a 200 of an accepted size.</summary>
    private async Task<Answer> SendAsync(
        string clientName, string address, Func<HttpContent> content, Action<HttpRequestMessage>? headers, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = content() };
        request.Headers.Accept.ParseAdd("application/json");
        headers?.Invoke(request);

        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waiting.CancelAfter(Patience);
        try
        {
            using var client = http.CreateClient(clientName);
            using var answer = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, waiting.Token);
            var takenAt = clock.GetUtcNow();
            return answer.StatusCode == HttpStatusCode.OK
                ? new Answer(answer.StatusCode, await BodyAsync(answer, waiting.Token), takenAt)
                : new Answer(answer.StatusCode, null, takenAt);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
            && ex is HttpRequestException or IOException or OperationCanceledException)
        {
            // No connection, an answer cut short, or too long to come. A caller that gave up is not a
            // failure of the source: its cancellation goes through as it is.
            throw new SourceException(
                SourceError.Unavailable, clientName == TokenClientName ? "Twitch did not answer" : "IGDB did not answer", ex);
        }
    }

    /// <summary>The bytes of an answer, counted on the way; null when it is larger than allowed.</summary>
    private static async Task<byte[]?> BodyAsync(HttpResponseMessage answer, CancellationToken ct)
    {
        if (answer.Content.Headers.ContentLength > MaxAnswerBytes)
        {
            return null;
        }

        await using var stream = await answer.Content.ReadAsStreamAsync(ct);
        using var kept = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (kept.Length + read > MaxAnswerBytes)
            {
                return null;
            }

            kept.Write(buffer, 0, read);
        }

        return kept.ToArray();
    }

    private static string Text(Answer answer, string host)
    {
        if (answer.Bytes is null)
        {
            throw new SourceException(
                SourceError.Unreadable,
                string.Create(CultureInfo.InvariantCulture, $"{host}'s answer is larger than {MaxAnswerBytes / (1024 * 1024)} MB"));
        }

        try
        {
            return Utf8.GetString(answer.Bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new SourceException(SourceError.Unreadable, $"{host}'s answer is not text", ex);
        }
    }

    /// <summary>What an answer other than 200 means. Twitch says 400 for a client id it does not know
    /// and 403 for a wrong secret; IGDB says 401 for a token it does not take.</summary>
    /// <param name="host">Who answered.</param>
    /// <param name="status">Its answer.</param>
    /// <param name="refused">What it refused when it says so.</param>
    private static SourceException Refusal(string host, HttpStatusCode status, string refused)
    {
        var code = (int)status;
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new SourceException(SourceError.Refused, $"{host} refused {refused} ({code})"),
            HttpStatusCode.BadRequest when host == "Twitch" =>
                new SourceException(SourceError.Refused, $"{host} refused {refused} ({code})"),
            HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError =>
                new SourceException(SourceError.Unavailable, $"{host} asked to come back later ({code})"),
            _ => new SourceException(SourceError.Unreadable, $"{host} answered {code}"),
        };
    }

    /// <summary>A token, the access it was given for, and when to ask for a new one.</summary>
    private sealed record Token((string ClientId, string ClientSecret) Access, string Value, DateTimeOffset RenewAt);

    private sealed record Answer(HttpStatusCode Status, byte[]? Bytes, DateTimeOffset TakenAt);
}
