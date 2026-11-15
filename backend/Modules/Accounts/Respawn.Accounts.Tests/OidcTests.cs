using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Accounts.Infrastructure.Oidc;
using Respawn.Accounts.Tests.Support;
using Respawn.Testing;

namespace Respawn.Accounts.Tests;

/// <summary>Sign-in with an ID token, against a provider answering from memory.</summary>
public sealed class OidcTests
{
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [Fact]
    public async Task The_config_lists_the_providers()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        var provider = (await browser.GetAsync("/api/v1/config").JsonAsync()).GetProperty("oidc_providers").EnumerateArray().Single();
        Assert.Equal("test", provider.GetProperty("name").GetString());
        Assert.Equal("https://issuer.test", provider.GetProperty("issuer").GetString());
        Assert.Equal("respawn-test", provider.GetProperty("client_id").GetString());
    }

    [Fact]
    public async Task A_valid_token_creates_the_first_account()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        var response = await browser.ExchangeAsync(issuer.Token());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var account = await response.JsonAsync();
        Assert.Equal(account.GetRawText(), (await browser.GetAsync("/api/v1/me").JsonAsync()).GetRawText());
        Assert.True(account.GetProperty("is_admin").GetBoolean());
        Assert.Equal("Player One", account.GetProperty("display_name").GetString());
    }

    [Fact]
    public async Task The_same_identity_signs_in_to_the_same_account()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var first = app.Browser();
        var account = await first.ExchangeAsync(issuer.Token()).JsonAsync();

        // Another token of the same subject, the e-mail changed meanwhile: resolved by issuer and subject.
        using var again = app.Browser();
        var response = await again.ExchangeAsync(issuer.Token(("email", "renamed@example.org"), ("iat", Now - 1)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(account.GetProperty("id").GetString(), (await response.JsonAsync()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task Refused_tokens()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        var refused = new Dictionary<string, string>
        {
            ["another audience"] = issuer.Token(("aud", "another-client")),
            ["another issuer"] = issuer.Token(("iss", "https://elsewhere.test")),
            ["expired"] = issuer.Token(("exp", Now - 600), ("iat", Now - 900)),
            ["another nonce"] = issuer.Token(("nonce", "nonce-other-value")),
            ["no nonce"] = issuer.Token(("nonce", null)),
            ["no subject"] = issuer.Token(("sub", null)),
            ["no expiry"] = issuer.Token(("exp", null)),
            ["signed by another key"] = issuer.TokenSignedBy(issuer.OtherKey),
            ["several audiences, for another client"] = issuer.Token(("aud", new[] { FakeIssuer.ClientId, "another-client" }), ("azp", "another-client")),
            ["not a token"] = "not.a.token",
        };
        foreach (var (reason, token) in refused)
        {
            var refusal = await browser.ExchangeAsync(token).RefusalAsync();
            Assert.True((HttpStatusCode.Unauthorized, "invalid_token") == refusal, $"{reason}: {refusal}");
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await browser.GetAsync("/api/v1/me").StatusAsync());
    }

    [Fact]
    public async Task An_unknown_provider()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        Assert.Equal(
            (HttpStatusCode.NotFound, "unknown_provider"),
            await browser.ExchangeAsync(issuer.Token(), provider: "nope").RefusalAsync());
    }

    [Fact]
    public async Task A_token_is_exchanged_once()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        var token = issuer.Token();
        using var first = app.Browser();
        Assert.Equal(HttpStatusCode.OK, await first.ExchangeAsync(token).StatusAsync());
        using var second = app.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, await second.ExchangeAsync(token).StatusAsync());
    }

    [Fact]
    public async Task A_new_identity_is_refused_when_registration_is_closed()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var owner = app.Browser();
        await owner.RegisterAsync();

        using var stranger = app.Browser();
        var token = issuer.Token(("sub", "someone-else"), ("email", "other@example.org"));
        Assert.Equal((HttpStatusCode.Forbidden, "registration_closed"), await stranger.ExchangeAsync(token).RefusalAsync());
    }

    [Fact]
    public async Task A_signed_in_account_links_the_provider()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        var account = await browser.RegisterAsync().JsonAsync();
        var linked = await browser.ExchangeAsync(issuer.Token(("email", "different@example.org")));
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        Assert.Equal(account.GetProperty("id").GetString(), (await linked.JsonAsync()).GetProperty("id").GetString());

        using var later = app.Browser();
        var again = await later.ExchangeAsync(issuer.Token(("iat", Now - 1))).JsonAsync();
        Assert.Equal(account.GetProperty("id").GetString(), again.GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_used_token_stays_refused_during_the_leeway()
    {
        // A token is accepted for a minute after it expires; its record must outlive that minute.
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        var token = issuer.Token(("exp", Now - 30), ("iat", Now - 300));
        using var first = app.Browser();
        Assert.Equal(HttpStatusCode.OK, await first.ExchangeAsync(token).StatusAsync());

        // Another sign-in runs the clean-up of the records.
        using var second = app.Browser();
        Assert.Equal(HttpStatusCode.OK, await second.ExchangeAsync(issuer.Token()).StatusAsync());
        using var third = app.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, await third.ExchangeAsync(token).StatusAsync());
    }

    [Theory]
    [InlineData(FakeIssuer.Issuer)]
    [InlineData(FakeIssuer.Issuer + "/")]
    public async Task An_issuer_with_a_trailing_slash(string configured)
    {
        // Configured with or without its final slash, a provider naming itself with one is accepted.
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync(new() { ["RESPAWN_OIDC_PROVIDERS"] = FakeIssuer.Providers(configured) });
        using var browser = app.Browser();
        var slashed = issuer.Token(("iss", FakeIssuer.Issuer + "/"));
        Assert.Equal(HttpStatusCode.OK, await browser.ExchangeAsync(slashed).StatusAsync());
        var doubled = issuer.Token(("iss", FakeIssuer.Issuer + "//"));
        Assert.Equal(HttpStatusCode.Unauthorized, await browser.ExchangeAsync(doubled).StatusAsync());
    }

    [Fact]
    public async Task Refused_tokens_fetch_nothing()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, await browser.ExchangeAsync("not.a.token").StatusAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, await browser.ExchangeAsync("garbage").StatusAsync());
        Assert.Equal(0, issuer.Requests); // not shaped like a token: the provider is not even asked for its keys

        Assert.Equal(HttpStatusCode.OK, await browser.ExchangeAsync(issuer.Token()).StatusAsync());
        Assert.Equal(2, issuer.Requests); // discovery and keys

        string[] refused =
        [
            "not.a.token",
            issuer.TokenSignedBy(issuer.OtherKey),
            issuer.Token(("aud", "another-client")),
            issuer.Token(("exp", Now - 600)),
        ];
        foreach (var token in refused)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await browser.ExchangeAsync(token).StatusAsync());
        }

        Assert.Equal(2, issuer.Requests);
    }

    [Fact]
    public async Task An_unknown_key_fetches_the_keys_again_but_not_at_will()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var first = app.Browser();
        Assert.Equal(HttpStatusCode.OK, await first.ExchangeAsync(issuer.Token()).StatusAsync());
        var fetched = issuer.Requests;
        issuer.Rotate();

        // The keys were fetched a moment ago: a token naming another key is refused without asking again.
        using var second = app.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, await second.ExchangeAsync(issuer.Token(("iat", Now - 1))).StatusAsync());
        Assert.Equal(fetched, issuer.Requests);

        // Once the minimum interval has passed, the new key is fetched and the token accepted.
        app.Services.GetRequiredService<OidcValidationSettings>().MinRefreshInterval = TimeSpan.Zero;
        Assert.Equal(HttpStatusCode.OK, await second.ExchangeAsync(issuer.Token(("iat", Now - 2))).StatusAsync());
        Assert.Equal(fetched + 2, issuer.Requests);
    }

    [Fact]
    public async Task An_unreachable_provider_is_not_asked_on_every_request()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        issuer.Down = HttpStatusCode.ServiceUnavailable;
        using var browser = app.Browser();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // Not a refused token: nothing could be checked.
            Assert.Equal(
                (HttpStatusCode.ServiceUnavailable, "provider_unavailable"),
                await browser.ExchangeAsync(issuer.Token()).RefusalAsync());
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await browser.ExchangeAsync("not.a.token").StatusAsync());
        Assert.Equal(1, issuer.Requests);
    }

    [Fact]
    public async Task A_provider_that_times_out_is_unavailable()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        issuer.TimesOut = true;
        using var browser = app.Browser();
        Assert.Equal(
            (HttpStatusCode.ServiceUnavailable, "provider_unavailable"),
            await browser.ExchangeAsync(issuer.Token()).RefusalAsync());
        Assert.Equal(0, app.Logs.Count("unhandled error"));
    }

    [Fact]
    public async Task A_caller_who_leaves_does_not_stop_the_fetch_of_the_keys()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        issuer.Held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The first caller gives up while the provider is being asked.
        using var impatient = app.Browser();
        using var leaving = new CancellationTokenSource();
        var abandoned = impatient.PostAsJsonAsync(
            "/api/v1/auth/oidc", new { provider = "test", id_token = issuer.Token(), nonce = FakeIssuer.Nonce }, leaving.Token);
        while (issuer.Requests == 0)
        {
            await Task.Delay(10);
        }

        // The request is told its caller left; the provider answers a moment later.
        await leaving.CancelAsync();
        await Task.Delay(200);
        issuer.Held.SetResult();
        issuer.Held = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        // The keys were fetched all the same: the next caller is signed in, not told to wait for the
        // next allowed fetch.
        using var browser = app.Browser();
        Assert.Equal(HttpStatusCode.OK, await browser.ExchangeAsync(issuer.Token(("iat", Now - 1))).StatusAsync());
        Assert.Equal(2, issuer.Requests); // discovery and keys, once
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(null, null)]
    [InlineData("true", "player@example.org")]
    public async Task Only_a_verified_e_mail_becomes_the_account_s(object? verified, string? expected)
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        var account = await browser.ExchangeAsync(issuer.Token(("email_verified", verified))).JsonAsync();
        Assert.Equal(expected, account.GetProperty("email").GetString());
        Assert.Equal("Player One", account.GetProperty("display_name").GetString());
    }

    [Fact]
    public async Task An_unverified_e_mail_does_not_take_the_address_of_an_account()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var owner = app.Browser();
        await owner.RegisterAsync("player@example.org");
        TestDatabase.Run("INSERT INTO accounts.settings (key, value) VALUES ('registration_open', 'true')");

        // Same address, not verified by the provider: a separate account, without the address.
        using var stranger = app.Browser();
        var account = await stranger.ExchangeAsync(issuer.Token(("email_verified", false))).JsonAsync();
        Assert.Equal(JsonValueKind.Null, account.GetProperty("email").ValueKind);
        Assert.False(account.GetProperty("is_admin").GetBoolean());
    }
}
