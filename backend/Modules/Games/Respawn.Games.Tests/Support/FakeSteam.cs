using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Steam;
using Respawn.Testing;

namespace Respawn.Games.Tests.Support;

/// <summary>
/// The Steam Web API answering from memory: a recorded answer, or whatever a test makes it say.
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

    /// <summary>When set, the answer's bytes, in place of <see cref="Body"/>.</summary>
    public byte[]? Bytes { get; set; }

    /// <summary>What the answer says it is.</summary>
    public string ContentType { get; set; } = "application/json; charset=utf-8";

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    /// <summary>When set, Steam never answers in time: the client's timeout fires.</summary>
    public bool TimesOut { get; set; }

    /// <summary>When set, Steam cannot be reached.</summary>
    public bool Unreachable { get; set; }

    /// <summary>The requests received, with the key each one carried in its header.</summary>
    public List<(Uri Address, string? Key)> Requests { get; } = [];

    /// <summary>An answer of <c>Recorded/steam</c>, as it is on disk.</summary>
    public static string Recorded(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", "steam", file));

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
            services?.Invoke(collection);
        });
    }

    private sealed class Transport(FakeSteam steam) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            steam.Requests.Add((
                request.RequestUri!,
                request.Headers.TryGetValues(SteamLibrarySource.KeyHeader, out var keys) ? string.Join(',', keys) : null));

            if (steam.TimesOut)
            {
                // What HttpClient throws when its timeout fires.
                throw new TaskCanceledException("The request was canceled due to the configured timeout.", new TimeoutException());
            }

            if (steam.Unreachable)
            {
                throw new HttpRequestException("No such host is known.");
            }

            var content = new ByteArrayContent(steam.Bytes ?? Encoding.UTF8.GetBytes(steam.Body));
            content.Headers.TryAddWithoutValidation("Content-Type", steam.ContentType);
            return Task.FromResult(new HttpResponseMessage(steam.Status) { Content = content });
        }
    }
}
