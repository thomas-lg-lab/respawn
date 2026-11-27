using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Respawn.Host.Configuration;
using Respawn.Host.Errors;
using Respawn.Host.Modules;
using Respawn.Testing;

namespace Respawn.Tests;

/// <summary>The host's own pieces, without starting the app: settings, the rate-limit key, the error handler.</summary>
public sealed class HostTests
{
    [Theory]
    [InlineData("RESPAWN_SESSION_DAYS", "0")]
    [InlineData("RESPAWN_PASSWORD_MIN_LENGTH", "-1")]
    [InlineData("RESPAWN_LOCKOUT_THRESHOLD", "many")]
    [InlineData("RESPAWN_OIDC_PROVIDERS", "not json")]
    [InlineData("RESPAWN_OIDC_PROVIDERS", """[{"name": "test", "client_id": "respawn"}]""")]
    [InlineData("RESPAWN_OIDC_PROVIDERS", """[{"name": "test", "issuer": "http://issuer.example.org", "client_id": "respawn"}]""")]
    [InlineData("RESPAWN_OIDC_PROVIDERS", "[null]")]
    [InlineData("RESPAWN_OIDC_PROVIDERS", """[{"name": "Test", "issuer": "https://issuer.example.org", "client_id": "respawn"}]""")]
    [InlineData("RESPAWN_OIDC_PROVIDERS", """[{"name": "a/b", "issuer": "https://issuer.example.org", "client_id": "respawn"}]""")]
    [InlineData("RESPAWN_OIDC_PROVIDERS", """[{"name": "sso", "issuer": "https://a.example.org", "client_id": "x"}, {"name": "sso", "issuer": "https://b.example.org", "client_id": "y"}]""")]
    [InlineData("RESPAWN_OIDC_PROVIDERS", """[{"name": "sso", "label": " ", "issuer": "https://issuer.example.org", "client_id": "respawn"}]""")]
    [InlineData("RESPAWN_OIDC_PROVIDERS", """[{"name": "sso", "issuer": "issuer.example.org", "client_id": "respawn"}]""")]
    public void An_account_setting_that_means_nothing_is_refused_by_name(string name, string value)
    {
        var error = Assert.Throws<FormatException>(() => RespawnSettings.Accounts(Configuration(name, value)));
        Assert.Contains(name, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RESPAWN_SESSION_DAYS", "0")]
    [InlineData("RESPAWN_OIDC_PROVIDERS", "not json")]
    [InlineData("RESPAWN_TRUSTED_PROXIES", "proxy.example.org")]
    [InlineData("RESPAWN_DATA_DIR", "covers")]
    [InlineData("RESPAWN_AUTH_RATE_LIMIT", "ten/minute")]
    [InlineData("RESPAWN_IGDB_GAMES_PER_TURN", "0")]
    public async Task A_setting_that_cannot_be_read_stops_the_start_and_the_log_names_it(string name, string value)
    {
        await using var app = TestApp.Create(new() { [name] = value });

        // However the host says it did not start: what matters is that it did not, and the line below.
        Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Equal(1, app.Logs.Count("a setting cannot be read, the app does not start"));
        Assert.Contains(name, app.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_data_directory_that_cannot_be_written_stops_the_start()
    {
        // A file where the directory should be: nothing can be made under it, whoever the app runs as.
        var file = Path.GetTempFileName();
        try
        {
            await using var app = TestApp.Create(new() { ["RESPAWN_DATA_DIR"] = file });
            Assert.ThrowsAny<Exception>(() => app.CreateClient());
            Assert.Equal(1, app.Logs.Count("the data directory (RESPAWN_DATA_DIR) cannot be written, the app does not start"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("0/minute")]
    [InlineData("ten/minute")]
    [InlineData("10/day")]
    public void A_rate_limit_that_allows_nothing_is_refused(string value)
    {
        var error = Assert.Throws<FormatException>(() => RespawnSettings.Http(Configuration("RESPAWN_AUTH_RATE_LIMIT", value)));
        Assert.Contains("RESPAWN_AUTH_RATE_LIMIT", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RESPAWN_COOKIE_SECURE", "treu")]
    [InlineData("RESPAWN_COOKIE_SECURE", "2")]
    [InlineData("RESPAWN_TRUSTED_PROXIES", "192.0.2.300")]
    [InlineData("RESPAWN_TRUSTED_PROXIES", "192.0.2.0/33")]
    [InlineData("RESPAWN_TRUSTED_PROXIES", "127.0.0.1,proxy.example.org")]
    public void An_http_setting_that_cannot_be_read_is_refused_by_name(string name, string value)
    {
        var error = Assert.Throws<FormatException>(() => RespawnSettings.Http(Configuration(name, value)));
        Assert.Contains(name, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData(" ON ", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("No", false)]
    [InlineData("0", false)]
    [InlineData("", true)]
    public void The_cookie_is_for_https_only_unless_plainly_said_otherwise(string value, bool secure) =>
        Assert.Equal(secure, RespawnSettings.Http(Configuration("RESPAWN_COOKIE_SECURE", value)).CookieSecure);

    [Fact]
    public void The_rate_limit_counts_an_ipv6_network_as_one_client()
    {
        // Two addresses of one /64 share a bucket; another /64 and an IPv4 address have their own.
        var first = AccountsModule.ClientKey(IPAddress.Parse("2001:db8:1:2::1"));
        Assert.Equal(first, AccountsModule.ClientKey(IPAddress.Parse("2001:db8:1:2:ffff:ffff:ffff:ffff")));
        Assert.NotEqual(first, AccountsModule.ClientKey(IPAddress.Parse("2001:db8:1:3::1")));
        Assert.Equal("203.0.113.7", AccountsModule.ClientKey(IPAddress.Parse("203.0.113.7")));
        Assert.Equal("203.0.113.7", AccountsModule.ClientKey(IPAddress.Parse("::ffff:203.0.113.7")));
        Assert.Equal("unknown", AccountsModule.ClientKey(null));
    }

    [Fact]
    public async Task A_request_its_client_gave_up_is_not_an_error()
    {
        using var logs = new LogCollector();
        using var factory = new LoggerFactory([logs]);
        var handler = new ApiExceptionHandler(factory.CreateLogger<ApiExceptionHandler>());
        using var gone = new CancellationTokenSource();
        await gone.CancelAsync();
        var context = new DefaultHttpContext { RequestAborted = gone.Token };

        Assert.True(await handler.TryHandleAsync(context, new OperationCanceledException(gone.Token), CancellationToken.None));
        Assert.Equal("", logs.Text);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode); // nothing was written
    }

    [Fact]
    public async Task An_error_is_logged_without_the_words_of_the_request()
    {
        using var logs = new LogCollector();
        using var factory = new LoggerFactory([logs]);
        var handler = new ApiExceptionHandler(factory.CreateLogger<ApiExceptionHandler>());
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/v1/x" + (char)10 + "2026-01-01T00:00:00Z crit: a forged line";
        context.Response.Body = new MemoryStream();

        Assert.True(await handler.TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains("unhandled error in no endpoint", logs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("forged", logs.Text, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(string name, string value) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [name] = value }).Build();

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", null)]
    [InlineData("https://respawn.example.org", "https://respawn.example.org/")]
    [InlineData("https://respawn.example.org/", "https://respawn.example.org/")]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    public void The_public_address_is_a_scheme_and_a_host(string? setting, string? expected) =>
        Assert.Equal(expected, RespawnSettings.Address(setting)?.AbsoluteUri);

    [Theory]
    [InlineData("respawn.example.org")]
    [InlineData("ftp://respawn.example.org")]
    [InlineData("https://respawn.example.org/app")]
    [InlineData("https://respawn.example.org/?x=1")]
    [InlineData("https://user:secret@respawn.example.org")]
    public void A_public_address_with_anything_else_is_refused_without_being_quoted(string setting)
    {
        var refusal = Assert.Throws<FormatException>(() => RespawnSettings.Address(setting));
        Assert.DoesNotContain("secret", refusal.Message, StringComparison.Ordinal);
    }
}
