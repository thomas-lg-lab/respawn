using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Steam;
using Respawn.Testing;

namespace Respawn.Games.Tests.Support;

/// <summary>
/// Steam answering from memory, a recorded answer or whatever a test makes it say: the Steam Web API,
/// and beside it the store's questions that take no key and the image host.
///
/// Only the network is replaced: the connector builds its real request, which is kept here to be
/// looked at, and reads the answer as it would Steam's.
/// </summary>
internal sealed class FakeSteam
{
    /// <summary>A key in the shape of Steam's: 32 letters and digits. Nobody's.</summary>
    public const string Key = "0123456789ABCDEF0123456789ABCDEF";

    /// <summary>What Steam answers next.</summary>
    public string Body { get; set; } = Recorded("owned-games.json");

    /// <summary>When set, what Steam answers a request, in place of <see cref="Body"/> when it gives one:
    /// a test answers each method, or each year, its own way.</summary>
    public Func<Uri, string?>? BodyFor { get; set; }

    /// <summary>When set, the answer's bytes, in place of <see cref="Body"/>.</summary>
    public byte[]? Bytes { get; set; }

    /// <summary>What the answer says it is.</summary>
    public string ContentType { get; set; } = "application/json; charset=utf-8";

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    /// <summary>When set, Steam never answers in time: the client's timeout fires.</summary>
    public bool TimesOut { get; set; }

    /// <summary>When set, Steam cannot be reached.</summary>
    public bool Unreachable { get; set; }

    /// <summary>When set, awaited once a request has arrived and before it is answered: a test holds
    /// Steam's answer back, or does something meanwhile.</summary>
    public Func<Task>? Before { get; set; }

    /// <summary>The requests received, with the key each one carried in its header.</summary>
    public List<(Uri Address, string? Key)> Requests { get; } = [];

    /// <summary>What Steam's store answers next, to the questions that take no key.</summary>
    public string StoreBody { get; set; } = Recorded("store-items.json");

    public HttpStatusCode StoreStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>When set, what the store answers a question with, by its rank among the questions
    /// received (the first is 0), in place of <see cref="StoreStatus"/> and <see cref="StoreBody"/>
    /// when it gives one.</summary>
    public Func<int, Uri, (HttpStatusCode Status, string Body)?>? StoreAnswerFor { get; set; }

    /// <summary>The questions Steam's store received, with the key each one carried: none, ever.</summary>
    public List<(Uri Address, string? Key)> StoreRequests { get; } = [];

    /// <summary>What Steam's image host has, by the file's path under the folder of the apps
    /// (<c>620/library_600x900.jpg</c>). Anything else is not found.</summary>
    public Dictionary<string, FakeImage> Images { get; } = [];

    /// <summary>When set, what the image host answers every request with, in place of its files.</summary>
    public HttpStatusCode? ImagesStatus { get; set; }

    /// <summary>The files asked of the image host, by the same path, with the key each request
    /// carried: none, ever.</summary>
    public List<(string Path, string? Key)> ImageRequests { get; } = [];

    /// <summary>An answer of <c>Recorded/steam</c>, as it is on disk.</summary>
    public static string Recorded(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", "steam", file));

    /// <summary>A file of <c>Recorded/steam</c> that is not text.</summary>
    public static byte[] RecordedBytes(string file) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Recorded", "steam", file));

    /// <summary>The app with its database, given a key and reaching this Steam in memory.</summary>
    public Task<TestApp> StartAppAsync(Dictionary<string, string?>? settings = null, Action<IServiceCollection>? services = null)
    {
        var all = new Dictionary<string, string?> { ["RESPAWN_STEAM_API_KEY"] = Key };
        foreach (var (key, value) in settings ?? [])
        {
            all[key] = value;
        }

        return TestApp.StartAsync(all, collection =>
        {
            collection.AddHttpClient(SteamLibrarySource.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Transport(this));
            collection.AddHttpClient(SteamCoverSource.StoreClientName).ConfigurePrimaryHttpMessageHandler(() => new StoreTransport(this));
            collection.AddHttpClient(SteamCoverSource.ImagesClientName).ConfigurePrimaryHttpMessageHandler(() => new ImagesTransport(this));
            services?.Invoke(collection);
        });
    }

    private sealed class Transport(FakeSteam steam) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            steam.Requests.Add((
                request.RequestUri!,
                request.Headers.TryGetValues(SteamLibrarySource.KeyHeader, out var keys) ? string.Join(',', keys) : null));

            if (steam.Before is { } before)
            {
                await before().WaitAsync(cancellationToken);
            }

            if (steam.TimesOut)
            {
                // What HttpClient throws when its timeout fires.
                throw new TaskCanceledException("The request was canceled due to the configured timeout.", new TimeoutException());
            }

            if (steam.Unreachable)
            {
                throw new HttpRequestException("No such host is known.");
            }

            var body = steam.BodyFor?.Invoke(request.RequestUri!) ?? steam.Body;
            var content = new ByteArrayContent(steam.Bytes ?? Encoding.UTF8.GetBytes(body));
            content.Headers.TryAddWithoutValidation("Content-Type", steam.ContentType);
            return new HttpResponseMessage(steam.Status) { Content = content };
        }
    }

    private static string? KeyOf(HttpRequestMessage request) =>
        request.Headers.TryGetValues(SteamLibrarySource.KeyHeader, out var keys) ? string.Join(',', keys) : null;

    private sealed class StoreTransport(FakeSteam steam) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = steam.StoreAnswerFor?.Invoke(steam.StoreRequests.Count, request.RequestUri!) ?? (steam.StoreStatus, steam.StoreBody);
            steam.StoreRequests.Add((request.RequestUri!, KeyOf(request)));
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            content.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=UTF-8");
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }

    private sealed class ImagesTransport(FakeSteam steam) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = SteamCoverSource.Images.MakeRelativeUri(request.RequestUri!).ToString();
            steam.ImageRequests.Add((path, KeyOf(request)));

            var image = steam.ImagesStatus is { } status ? new FakeImage([], "text/html", status)
                : steam.Images.GetValueOrDefault(path) ?? new FakeImage("<html>404 Not Found</html>"u8.ToArray(), "text/html", HttpStatusCode.NotFound);
            var answer = new HttpResponseMessage(image.Status) { Content = new ByteArrayContent(image.Bytes) };
            answer.Content.Headers.TryAddWithoutValidation("Content-Type", image.ContentType);
            if (image.Location is not null)
            {
                answer.Headers.Location = new Uri(image.Location);
            }

            return Task.FromResult(answer);
        }
    }
}

/// <summary>A file of Steam's image host, as it answers for it.</summary>
internal sealed record FakeImage(byte[] Bytes, string ContentType = "image/jpeg", HttpStatusCode Status = HttpStatusCode.OK, string? Location = null);
