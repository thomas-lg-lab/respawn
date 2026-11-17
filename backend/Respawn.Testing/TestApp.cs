using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Respawn.Testing;

/// <summary>The real app, started in-process: routing, validation, sign-in, EF Core and migrations.
/// Shared by every test project; what only one module needs lives in that module's tests.</summary>
internal sealed class TestApp : WebApplicationFactory<Program>
{
    public const string Password = "correct horse battery";

    private readonly Dictionary<string, string?> _settings;
    private readonly Action<IServiceCollection>? _services;

    private TestApp(Dictionary<string, string?> settings, Action<IServiceCollection>? services)
    {
        _settings = settings;
        _services = services;
    }

    /// <summary>A stand-in for the front's build, which the image brings and the repository does not hold:
    /// a document, files named after their content and files that are not, and the page of the API's
    /// description.</summary>
    private static readonly Lazy<string> FrontBuild = new(() =>
    {
        var root = Directory.CreateTempSubdirectory("respawn-front-").FullName;
        File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>respawn</title><rs-root ngCspNonce=\"RESPAWN_CSP_NONCE\"></rs-root>");
        File.WriteAllText(Path.Combine(root, "main-QX2ZHB7T.js"), "");
        File.WriteAllText(Path.Combine(root, "chunk-f7g-vKq6.js"), "");
        File.WriteAllText(Path.Combine(root, "theme.js"), "");
        File.WriteAllText(Path.Combine(root, "user-settings.js"), "");
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        File.WriteAllText(Path.Combine(root, "docs", "index.html"), "<!doctype html><title>respawn API</title><div id=\"swagger-ui\"></div>");
        File.WriteAllText(Path.Combine(root, "docs", "init.js"), "");
        File.WriteAllText(Path.Combine(root, "docs", "LICENSE"), "The licence of a library the page ships.");
        File.WriteAllText(Path.Combine(root, "docs", "bundle.unknown"), "");
        File.WriteAllText(Path.Combine(root, "favicon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        return root;
    });

    public LogCollector Logs { get; } = new();

    /// <summary>The address requests seem to come from: the visitor's, or the reverse proxy's.</summary>
    public IPAddress Peer { get; set; } = IPAddress.Parse("203.0.113.200");

    /// <summary>The app with its database, migrated and emptied for the test.</summary>
    public static async Task<TestApp> StartAsync(
        Dictionary<string, string?>? settings = null, Action<IServiceCollection>? services = null)
    {
        TestDatabase.EnsureCreated();
        var app = new TestApp(Settings(settings), services);
        // The first request migrates the schema; the tables are then emptied for the test.
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/config")).StatusCode);
        TestDatabase.Truncate();
        return app;
    }

    /// <summary>The app as it starts, without touching anything: no request is sent, no table emptied.</summary>
    public static TestApp Create(Dictionary<string, string?>? settings = null, Action<IServiceCollection>? services = null) =>
        new(Settings(settings), services);

    /// <summary>The app as it starts with an empty secrets directory and no database.</summary>
    public static TestApp Offline(string? urlFile = null) => Create(new()
    {
        ["RESPAWN_DATABASE_URL"] = null,
        ["RESPAWN_DATABASE_URL_FILE"] = urlFile ?? Path.Combine(Path.GetTempPath(), $"respawn-no-secret-{Guid.NewGuid():N}"),
    });

    /// <summary>A browser of its own: cookies are kept from one request to the next.</summary>
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseWebRoot(FrontBuild.Value);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_settings));
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter>(new PeerFilter(this));
            _services?.Invoke(services);
        });
    }

    private static Dictionary<string, string?> Settings(Dictionary<string, string?>? overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["RESPAWN_DATABASE_URL"] = TestDatabase.Url,
            ["RESPAWN_COOKIE_SECURE"] = "false",
            ["RESPAWN_LOCKOUT_THRESHOLD"] = "3",
            ["RESPAWN_AUTH_RATE_LIMIT"] = "1000/minute",
        };
        foreach (var (key, value) in overrides ?? [])
        {
            settings[key] = value;
        }

        return settings;
    }

    /// <summary>The in-process server has no socket, hence no peer address: this gives it one.</summary>
    private sealed class PeerFilter(TestApp app) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => builder =>
        {
            builder.Use((context, following) =>
            {
                context.Connection.RemoteIpAddress = app.Peer;
                return following(context);
            });
            next(builder);
        };
    }
}

/// <summary>Everything the app logs, for the tests that read the log.</summary>
internal sealed class LogCollector : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public string Text => string.Join('\n', _lines);

    public int Count(string fragment) => _lines.Count(line => line.Contains(fragment, StringComparison.Ordinal));

    public ILogger CreateLogger(string categoryName) => new Collector(_lines);

    public void Dispose()
    {
    }

    private sealed class Collector(ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{logLevel} {formatter(state, exception)} {exception}");
    }
}

internal static class ApiCalls
{
    public static Task<HttpResponseMessage> RegisterAsync(
        this HttpClient client, string email = "first@example.org", string name = "First") =>
        client.PostAsJsonAsync("/api/v1/auth/register", new { email, display_name = name, password = TestApp.Password });

    public static Task<HttpResponseMessage> LoginAsync(
        this HttpClient client, string email, string password, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, password }),
        };
        foreach (var (name, value) in headers)
        {
            if (name == "Host")
            {
                request.Headers.Host = value;
            }
            else
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return client.SendAsync(request);
    }

    public static async Task<JsonElement> JsonAsync(this Task<HttpResponseMessage> response) =>
        await (await response).Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>The status and the error code of a refusal.</summary>
    public static async Task<(HttpStatusCode Status, string? Code)> RefusalAsync(this Task<HttpResponseMessage> response)
    {
        var answer = await response;
        var body = await answer.JsonAsync();
        return (answer.StatusCode, body.GetProperty("error").GetProperty("code").GetString());
    }

    public static async Task<HttpStatusCode> StatusAsync(this Task<HttpResponseMessage> response) => (await response).StatusCode;
}
