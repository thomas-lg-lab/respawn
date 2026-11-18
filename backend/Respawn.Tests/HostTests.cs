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
    public void An_account_setting_that_means_nothing_is_refused_by_name(string name, string value)
    {
        var error = Assert.Throws<FormatException>(() => RespawnSettings.Accounts(Configuration(name, value)));
        Assert.Contains(name, error.Message, StringComparison.Ordinal);
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
