using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Igdb;
using Respawn.Testing;

namespace Respawn.Games.Tests.Support;

/// <summary>
/// IGDB answering from memory, a recorded answer or whatever a test makes it say, and beside it Twitch,
/// which gives the tokens.
///
/// Only the network is replaced: the connector builds its real requests, which are kept here to be
/// looked at, and reads the answers as it would IGDB's and Twitch's.
/// </summary>
internal sealed class FakeIgdb
{
    /// <summary>A client id and a secret in the shape of Twitch's: 30 letters and digits. Nobody's.</summary>
    public const string ClientId = "fakeclient0nobodys0client00000";

    public const string ClientSecret = "fakesecret0nobodys0secret00000";

    /// <summary>The token in the recorded answer of Twitch, which stands for the one it gave.</summary>
    private const string RecordedToken = "fake0token0nobodys0token000000";

    /// <summary>What Twitch answers next; each token it gives is another (<see cref="TokenOf"/>).</summary>
    public string TokenBody { get; set; } = Recorded("token.json");

    public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>The requests Twitch received, with their form.</summary>
    public List<(Uri Address, string Form)> TokenRequests { get; } = [];

    /// <summary>What IGDB answers next about the games of the stores (<c>external_games</c>).</summary>
    public string Body { get; set; } = Recorded("external-games.json");

    /// <summary>What IGDB answers next about its games (<c>games</c>).</summary>
    public string GamesBody { get; set; } = Recorded("games.json");

    /// <summary>What IGDB answers next about how long they take (<c>game_time_to_beats</c>).</summary>
    public string TimesBody { get; set; } = Recorded("game-time-to-beats.json");

    /// <summary>The status of every answer of IGDB.</summary>
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    /// <summary>When set, what IGDB answers a question with, by its rank among the questions received
    /// (the first is 0), in place of <see cref="Status"/> and <see cref="Body"/> when it gives one.</summary>
    public Func<int, string, (HttpStatusCode Status, string Body)?>? AnswerFor { get; set; }

    /// <summary>When set, IGDB cannot be reached.</summary>
    public bool Unreachable { get; set; }

    /// <summary>The questions IGDB received.</summary>
    public List<IgdbRequest> Requests { get; } = [];

    /// <summary>The token Twitch gives the n-th time it is asked (the first is 0).</summary>
    public static string TokenOf(int rank) => "fake0token0nobodys0token" + rank.ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>An answer of <c>Recorded/igdb</c>, as it is on disk.</summary>
    public static string Recorded(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", "igdb", file));

    /// <summary>The images of IGDB's image host, by their path under the host's folder
    /// (<c>t_cover_big_2x/co0fake71.jpg</c>). A path not here answers 404.</summary>
    public Dictionary<string, FakeImage> Images { get; } = [];

    /// <summary>The paths the image host was asked for, with the authorization each carried.</summary>
    public List<(string Path, string? Authorization, string? ClientId)> ImageRequests { get; } = [];

    /// <summary>The settings that give the instance this IGDB's client id and secret.</summary>
    public static Dictionary<string, string?> Settings() => new()
    {
        ["RESPAWN_IGDB_CLIENT_ID"] = ClientId,
        ["RESPAWN_IGDB_CLIENT_SECRET"] = ClientSecret,
    };

    /// <summary>The app with its database, given a client id and secret and reaching this IGDB in memory.</summary>
    public Task<TestApp> StartAppAsync(Dictionary<string, string?>? settings = null, Action<IServiceCollection>? services = null)
    {
        var all = Settings();
        foreach (var (key, value) in settings ?? [])
        {
            all[key] = value;
        }

        return TestApp.StartAsync(all, collection =>
        {
            Reach(collection);
            services?.Invoke(collection);
        });
    }

    /// <summary>Makes the app reach this IGDB in memory: Twitch, the API and the image host.</summary>
    public void Reach(IServiceCollection collection)
    {
        collection.AddHttpClient(IgdbClient.TokenClientName).ConfigurePrimaryHttpMessageHandler(() => new TwitchTransport(this));
        collection.AddHttpClient(IgdbClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Transport(this));
        collection.AddHttpClient(IgdbImages.ClientName).ConfigurePrimaryHttpMessageHandler(() => new ImagesTransport(this));
    }

    private sealed class ImagesTransport(FakeIgdb igdb) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath["/igdb/image/upload/".Length..];
            lock (igdb.ImageRequests)
            {
                igdb.ImageRequests.Add((
                    path,
                    request.Headers.Authorization?.ToString(),
                    request.Headers.TryGetValues("Client-ID", out var ids) ? string.Join(',', ids) : null));
            }

            if (!igdb.Images.TryGetValue(path, out var image))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var content = new ByteArrayContent(image.Bytes);
            content.Headers.TryAddWithoutValidation("Content-Type", image.ContentType);
            return Task.FromResult(new HttpResponseMessage(image.Status) { Content = content });
        }
    }

    private static HttpResponseMessage Answer(HttpStatusCode status, string body)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        return new HttpResponseMessage(status) { Content = content };
    }

    private sealed class TwitchTransport(FakeIgdb igdb) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            int rank;
            lock (igdb.TokenRequests)
            {
                rank = igdb.TokenRequests.Count;
                igdb.TokenRequests.Add((request.RequestUri!, form));
            }

            return Answer(igdb.TokenStatus, igdb.TokenBody.Replace(RecordedToken, TokenOf(rank), StringComparison.Ordinal));
        }
    }

    private sealed class Transport(FakeIgdb igdb) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = await request.Content!.ReadAsStringAsync(cancellationToken);
            int rank;
            lock (igdb.Requests)
            {
                rank = igdb.Requests.Count;
                igdb.Requests.Add(new IgdbRequest(
                    request.Method,
                    request.RequestUri!,
                    request.Headers.TryGetValues("Client-ID", out var ids) ? string.Join(',', ids) : null,
                    request.Headers.Authorization?.ToString(),
                    query));
            }

            if (igdb.Unreachable)
            {
                throw new HttpRequestException("No such host is known.");
            }

            var (status, body) = igdb.AnswerFor?.Invoke(rank, query) ?? (igdb.Status, request.RequestUri!.AbsolutePath switch
            {
                "/v4/games" => igdb.GamesBody,
                "/v4/game_time_to_beats" => igdb.TimesBody,
                _ => igdb.Body,
            });
            return Answer(status, body);
        }
    }
}

/// <summary>A question IGDB received: its endpoint, the client id and token it carried, and the query.</summary>
internal sealed record IgdbRequest(HttpMethod Method, Uri Address, string? ClientId, string? Authorization, string Query);
