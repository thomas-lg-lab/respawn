using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Respawn.Accounts.Tests.Support;
using Respawn.Testing;

namespace Respawn.Accounts.Tests;

/// <summary>Signing in through a provider, run by the API, against a provider answering from memory.</summary>
public sealed class OidcTests
{
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [Fact]
    public async Task The_config_lists_the_providers_by_name_and_label()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync(new() { ["RESPAWN_OIDC_PROVIDERS"] = FakeIssuer.Providers(FakeIssuer.Issuer, "Test SSO") });
        using var browser = app.Browser();
        var provider = (await browser.GetAsync("/api/v1/config").JsonAsync()).GetProperty("oidc_providers").EnumerateArray().Single();
        Assert.Equal("""{"name":"test","label":"Test SSO"}""", provider.GetRawText());
    }

    [Fact]
    public async Task Over_https_the_cookies_of_a_sign_in_through_a_provider_are_this_hosts_alone()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync(new()
        {
            ["RESPAWN_OIDC_PROVIDERS"] = FakeIssuer.Providers(FakeIssuer.Issuer, "Test SSO"),
            ["RESPAWN_COOKIE_SECURE"] = "true",
        });
        using var browser = app.Browser();
        var start = await browser.StartAsync("/library");
        Assert.Equal(HttpStatusCode.Found, start.StatusCode);

        // The pair that ties the provider's answer to this browser: neither can be planted from a
        // neighbouring subdomain, which the prefix forbids, with Secure and the whole site as path.
        var cookies = start.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Equal(2, cookies.Length);
        Assert.All(cookies, cookie =>
        {
            Assert.StartsWith("__Host-.AspNetCore.", cookie, StringComparison.Ordinal);
            Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("; path=/;", cookie + ";", StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task The_browser_is_sent_to_the_provider_for_a_code_with_pkce()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        var start = await browser.StartAsync("/library");
        Assert.Equal(HttpStatusCode.Found, start.StatusCode);
        Assert.Equal("no-store", start.Headers.CacheControl?.ToString());

        var asked = QueryHelpers.ParseQuery(start.Headers.Location!.Query);
        Assert.Equal(FakeIssuer.ClientId, asked["client_id"]);
        Assert.Equal("code", asked["response_type"]);
        // Answered in the address: query is the default of the code flow, so it may go unsaid.
        Assert.True(!asked.ContainsKey("response_mode") || asked["response_mode"] == "query");
        Assert.Equal("S256", asked["code_challenge_method"]);
        Assert.Equal("http://localhost/api/v1/auth/oidc/test/callback", asked["redirect_uri"]);
        Assert.Equal(["openid", "email", "profile"], asked["scope"].ToString().Split(' '));
        Assert.False(string.IsNullOrEmpty(asked["nonce"]));
        Assert.False(string.IsNullOrEmpty(asked["code_challenge"]));
        Assert.Contains(start.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_proven_identity_creates_the_first_account_and_signs_in()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        var back = await browser.SignInAsync(issuer, "/sign-in?returnTo=%2Flibrary");
        Assert.Equal("/sign-in?returnTo=%2Flibrary", back.Landing());

        var account = await browser.GetAsync("/api/v1/me").JsonAsync();
        Assert.True(account.GetProperty("is_admin").GetBoolean());
        Assert.Equal("Player One", account.GetProperty("display_name").GetString());
        Assert.Equal("player@example.org", account.GetProperty("email").GetString());

        // The code was redeemed with its verifier, by a public client: no secret was sent.
        var redemption = Assert.Single(issuer.Redemptions);
        Assert.True(redemption.ContainsKey("code_verifier"));
        Assert.False(redemption.ContainsKey("client_secret"));
    }

    [Fact]
    public async Task The_same_identity_signs_in_to_the_same_account()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var first = app.Browser();
        await first.SignInAsync(issuer);
        var account = await first.GetAsync("/api/v1/me").JsonAsync();

        // Another sign-in of the same subject, the e-mail changed meanwhile: resolved by issuer and subject.
        using var again = app.Browser();
        Assert.Equal("/", (await again.SignInAsync(issuer, "/", ("email", "renamed@example.org"))).Landing());
        Assert.Equal(account.GetProperty("id").GetString(), (await again.GetAsync("/api/v1/me").JsonAsync()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_new_identity_is_turned_away_when_registration_is_closed()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var owner = app.Browser();
        await owner.RegisterAsync();

        using var stranger = app.Browser();
        var back = await stranger.SignInAsync(issuer, "/sign-in", ("sub", "someone-else"), ("email", "other@example.org"));
        Assert.Equal("/sign-in?provider_sign_in=registration_closed", back.Landing());
        Assert.Equal(HttpStatusCode.Unauthorized, await stranger.GetAsync("/api/v1/me").StatusAsync());
    }

    [Fact]
    public async Task A_provider_that_creates_accounts_lets_in_a_new_member_while_registration_is_closed()
    {
        var issuer = new FakeIssuer();
        var providers = """[{"name": "test", "issuer": "https://issuer.test", "client_id": "respawn-test", "create_accounts": true}]""";
        await using var app = await issuer.StartAppAsync(new() { ["RESPAWN_OIDC_PROVIDERS"] = providers });
        using var owner = app.Browser();
        await owner.RegisterAsync();

        // The provider's own policy said who may come in: a member account, not an administrator.
        using var member = app.Browser();
        Assert.Equal("/", (await member.SignInAsync(issuer, "/", ("sub", "someone-else"), ("email", "other@example.org"))).Landing());
        var account = await member.GetAsync("/api/v1/me").JsonAsync();
        Assert.False(account.GetProperty("is_admin").GetBoolean());
        Assert.Equal("other@example.org", account.GetProperty("email").GetString());

        // Local registration stays closed.
        Assert.False((await member.GetAsync("/api/v1/config").JsonAsync()).GetProperty("registration_open").GetBoolean());
    }

    [Fact]
    public async Task The_verified_e_mail_of_an_account_is_not_taken_by_a_new_identity()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var owner = app.Browser();
        await owner.RegisterAsync("player@example.org");
        TestDatabase.OpenRegistration();

        // Never attached by e-mail alone: the owner signs in first, then links the provider.
        using var stranger = app.Browser();
        Assert.Equal("/?provider_sign_in=link_provider_first", (await stranger.SignInAsync(issuer)).Landing());
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
        await browser.SignInAsync(issuer, "/", ("email_verified", verified));
        var account = await browser.GetAsync("/api/v1/me").JsonAsync();
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
        TestDatabase.OpenRegistration();

        // Same address, not verified by the provider: a separate account, without the address.
        using var stranger = app.Browser();
        Assert.Equal("/", (await stranger.SignInAsync(issuer, "/", ("email_verified", false))).Landing());
        var account = await stranger.GetAsync("/api/v1/me").JsonAsync();
        Assert.Equal(JsonValueKind.Null, account.GetProperty("email").ValueKind);
        Assert.False(account.GetProperty("is_admin").GetBoolean());
    }

    [Fact]
    public async Task A_signed_in_account_links_the_provider()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        var account = await browser.RegisterAsync().JsonAsync();
        var back = await browser.LinkAsync(issuer, "/library?tab=all", ("email", "different@example.org"));
        Assert.Equal("/library?tab=all&provider_sign_in=linked", back.Landing());
        // Still the same session, of the same account.
        Assert.Equal(account.GetProperty("id").GetString(), (await browser.GetAsync("/api/v1/me").JsonAsync()).GetProperty("id").GetString());

        // From now on the provider signs in to that account.
        using var later = app.Browser();
        await later.SignInAsync(issuer);
        Assert.Equal(account.GetProperty("id").GetString(), (await later.GetAsync("/api/v1/me").JsonAsync()).GetProperty("id").GetString());

        // Linking it again changes nothing.
        Assert.Equal("/?provider_sign_in=linked", (await browser.LinkAsync(issuer)).Landing());
    }

    [Fact]
    public async Task The_account_says_which_providers_it_is_linked_to()
    {
        var issuer = new FakeIssuer();
        var providers = $$"""
            [{"name": "test", "label": "Test SSO", "issuer": "{{FakeIssuer.Issuer}}/", "client_id": "{{FakeIssuer.ClientId}}"},
             {"name": "other", "issuer": "https://other.test", "client_id": "respawn-test"}]
            """;
        await using var app = await issuer.StartAppAsync(new() { ["RESPAWN_OIDC_PROVIDERS"] = providers });
        using var browser = app.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, await browser.GetAsync("/api/v1/me/providers").StatusAsync());

        await browser.RegisterAsync();
        Assert.Equal(
            """[{"name":"test","label":"Test SSO","linked":false},{"name":"other","label":"other","linked":false}]""",
            (await browser.GetAsync("/api/v1/me/providers").JsonAsync()).GetRawText());

        // Linked under the issuer as configured, trailing slash or not.
        await browser.LinkAsync(issuer);
        Assert.Equal(
            """[{"name":"test","label":"Test SSO","linked":true},{"name":"other","label":"other","linked":false}]""",
            (await browser.GetAsync("/api/v1/me/providers").JsonAsync()).GetRawText());

        // Another account of the instance is not.
        TestDatabase.OpenRegistration();
        using var someone = app.Browser();
        await someone.RegisterAsync("someone@example.org");
        Assert.All(
            (await someone.GetAsync("/api/v1/me/providers").JsonAsync()).EnumerateArray(),
            provider => Assert.False(provider.GetProperty("linked").GetBoolean()));
    }

    [Fact]
    public async Task A_link_needs_a_session()
    {
        // A navigation: nobody signed in (the session ended in a tab left open) goes back to the app, not
        // to an error.
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        Assert.Equal("/library?provider_sign_in=failed", (await browser.StartAsync("/library", link: true)).Landing());
        Assert.Equal(0, issuer.Requests);
    }

    [Fact]
    public async Task A_sign_in_closes_the_session_the_browser_carried()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        var registered = await browser.RegisterAsync();
        var carried = registered.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith("respawn_session=", StringComparison.Ordinal)).Split(';')[0];
        TestDatabase.OpenRegistration();

        // Someone else signs in through the provider in the same browser: the cookie now names their session.
        Assert.Equal("/", (await browser.SignInAsync(issuer, "/", ("email", "other@example.org"))).Landing());
        Assert.Equal("other@example.org", (await browser.GetAsync("/api/v1/me").JsonAsync()).GetProperty("email").GetString());

        // The session it replaced is closed, not left good until it expires.
        using var elsewhere = app.Browser();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Add("Cookie", carried);
        Assert.Equal(HttpStatusCode.Unauthorized, await elsewhere.SendAsync(request).StatusAsync());
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("two words@example.org")]
    [InlineData("long")]
    public async Task A_verified_e_mail_that_is_not_an_address_is_not_the_account_s(string claimed)
    {
        // Whatever the provider says of it: the account is created, without an address.
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        var email = claimed == "long" ? new string('a', 320) + "@example.org" : claimed;
        Assert.Equal("/", (await browser.SignInAsync(issuer, "/", ("email", email))).Landing());
        var account = await browser.GetAsync("/api/v1/me").JsonAsync();
        Assert.Equal(JsonValueKind.Null, account.GetProperty("email").ValueKind);
        Assert.Equal("Player One", account.GetProperty("display_name").GetString());
        Assert.Equal(0, app.Logs.Count("unhandled error"));
    }

    [Fact]
    public async Task Too_many_starts_or_returns_go_back_to_the_app_and_each_has_its_own_count()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync(new() { ["RESPAWN_AUTH_RATE_LIMIT"] = "2/minute" });
        using var browser = app.Browser();
        var first = await browser.StartAsync("/sign-in");
        var second = await browser.StartAsync("/sign-in");
        Assert.Equal(HttpStatusCode.Found, second.StatusCode);

        // A third start in the minute: a navigation, so the app with a word, never a page of JSON.
        var third = await browser.StartAsync("/sign-in?returnTo=%2Flibrary");
        Assert.Equal("/sign-in?returnTo=%2Flibrary&provider_sign_in=failed", third.Landing());
        Assert.Equal("no-store", third.Headers.CacheControl?.ToString());

        // The returns are counted apart: the starts did not use them up.
        Assert.Equal("/sign-in", (await browser.GetAsync(new Uri(issuer.Authorize(first.Headers.Location!, [])).PathAndQuery)).Landing());
        var asked = issuer.Requests;

        // Returns nobody started, written by hand: two are answered, the next ones are not even read, so
        // they cost neither a question to the provider nor a line of the log.
        using var other = app.Browser();
        Assert.Equal("/?provider_sign_in=failed", (await other.GetAsync(ProviderCalls.Start + "/callback?error=x&error_description=y&state=z")).Landing());
        var said = app.Logs.Count("did not complete");
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal("/?provider_sign_in=failed", (await other.GetAsync(ProviderCalls.Start + "/callback?error=x&error_description=y&state=z")).Landing());
        }

        Assert.Equal(said, app.Logs.Count("did not complete"));
        Assert.Equal(asked, issuer.Requests);
    }

    [Fact]
    public async Task A_sign_in_never_links_whatever_session_the_browser_carries()
    {
        // A session planted in the browser (another site of the same parent domain can set the cookie) must
        // not turn the visitor's own sign-in into a link of their identity to that account.
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var planted = app.Browser();
        var attacker = (await planted.RegisterAsync().JsonAsync()).GetProperty("id").GetString();

        // The same browser, so the same cookie, signs in through the provider: registration is closed and the
        // identity is new, so it is turned away, and not attached to the account the cookie names.
        Assert.Equal("/?provider_sign_in=registration_closed", (await planted.SignInAsync(issuer)).Landing());
        TestDatabase.OpenRegistration();
        using var victim = app.Browser();
        await victim.SignInAsync(issuer);
        Assert.NotEqual(attacker, (await victim.GetAsync("/api/v1/me").JsonAsync()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task An_identity_linked_to_another_account_is_not_moved()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var first = app.Browser();
        await first.SignInAsync(issuer);
        var owner = (await first.GetAsync("/api/v1/me").JsonAsync()).GetProperty("id").GetString();
        TestDatabase.OpenRegistration();

        using var second = app.Browser();
        await second.RegisterAsync("second@example.org", "Second");
        Assert.Equal("/?provider_sign_in=linked_elsewhere", (await second.LinkAsync(issuer)).Landing());

        using var again = app.Browser();
        await again.SignInAsync(issuer);
        Assert.Equal(owner, (await again.GetAsync("/api/v1/me").JsonAsync()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_link_comes_back_to_the_session_that_asked_or_fails()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        TestDatabase.OpenRegistration();

        // Signed out while at the provider: the link is not made, and no session is opened either.
        using var browser = app.Browser();
        await browser.RegisterAsync();
        var start = await browser.StartAsync(link: true);
        await browser.PostAsync("/api/v1/auth/logout", null);
        var back = await browser.GetAsync(new Uri(issuer.Authorize(start.Headers.Location!, [])).PathAndQuery);
        Assert.Equal("/?provider_sign_in=failed", back.Landing());
        Assert.Equal(HttpStatusCode.Unauthorized, await browser.GetAsync("/api/v1/me").StatusAsync());

        // Another account signed in while at the provider: not linked to either.
        using var first = app.Browser();
        await first.RegisterAsync("a@example.org", "A");
        start = await first.StartAsync(link: true);
        await first.PostAsync("/api/v1/auth/logout", null);
        await first.LoginAsync("first@example.org", TestApp.Password);
        back = await first.GetAsync(new Uri(issuer.Authorize(start.Headers.Location!, [])).PathAndQuery);
        Assert.Equal("/?provider_sign_in=failed", back.Landing());
        using var nobody = app.Browser();
        await nobody.SignInAsync(issuer, "/", ("email", "fresh@example.org"));
        var created = await nobody.GetAsync("/api/v1/me").JsonAsync();
        Assert.Equal("fresh@example.org", created.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Tokens_the_handler_refuses()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        var refused = new Dictionary<string, ((string, object?)[] Claims, bool OtherKey)>
        {
            ["another audience"] = ([("aud", "another-client")], false),
            ["another issuer"] = ([("iss", "https://elsewhere.test")], false),
            ["a doubled slash in the issuer"] = ([("iss", FakeIssuer.Issuer + "//")], false),
            ["expired"] = ([("exp", Now - 600), ("iat", Now - 900)], false),
            ["another nonce"] = ([("nonce", "another-nonce")], false),
            ["no nonce"] = ([("nonce", null)], false),
            ["no subject"] = ([("sub", null)], false),
            ["several audiences, for another client"] = ([("aud", new[] { FakeIssuer.ClientId, "another-client" }), ("azp", "another-client")], false),
            ["signed by another key"] = ([], true),
        };
        foreach (var (reason, (claims, otherKey)) in refused)
        {
            using var browser = app.Browser();
            var start = await browser.StartAsync("/sign-in");
            var back = await browser.GetAsync(
                new Uri(issuer.Authorize(start.Headers.Location!, claims, otherKey ? issuer.OtherKey : null)).PathAndQuery);
            Assert.True(back.Landing() == "/sign-in?provider_sign_in=failed", $"{reason}: {back.Headers.Location}");
            Assert.Equal(HttpStatusCode.Unauthorized, await browser.GetAsync("/api/v1/me").StatusAsync());
        }
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
        Assert.Equal("/", (await browser.SignInAsync(issuer, "/", ("iss", FakeIssuer.Issuer + "/"))).Landing());
    }

    [Fact]
    public async Task A_return_in_another_browser_proves_nothing()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var visitor = app.Browser();
        var start = await visitor.StartAsync("/library");
        var comingBack = new Uri(issuer.Authorize(start.Headers.Location!, [])).PathAndQuery;

        // The address the provider sent back to, followed by a browser that did not start: no correlation
        // cookie, no sign-in. It still lands where the start asked.
        using var other = app.Browser();
        Assert.Equal("/library?provider_sign_in=failed", (await other.GetAsync(comingBack)).Landing());
        Assert.Equal(HttpStatusCode.Unauthorized, await other.GetAsync("/api/v1/me").StatusAsync());
    }

    [Fact]
    public async Task A_return_is_good_once()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        var start = await browser.StartAsync();
        var comingBack = new Uri(issuer.Authorize(start.Headers.Location!, [])).PathAndQuery;
        Assert.Equal("/", (await browser.GetAsync(comingBack)).Landing());
        await browser.PostAsync("/api/v1/auth/logout", null);

        Assert.Equal("/?provider_sign_in=failed", (await browser.GetAsync(comingBack)).Landing());
        Assert.Equal(HttpStatusCode.Unauthorized, await browser.GetAsync("/api/v1/me").StatusAsync());
    }

    [Fact]
    public async Task The_provider_turns_the_visitor_away()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        var start = await browser.StartAsync("/sign-in?returnTo=%2Flibrary");
        var back = await browser.GetAsync(new Uri(FakeIssuer.Deny(start.Headers.Location!)).PathAndQuery);
        Assert.Equal("/sign-in?returnTo=%2Flibrary&provider_sign_in=denied", back.Landing());
    }

    [Fact]
    public async Task A_provider_that_cannot_be_asked_is_unavailable()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();

        // Its discovery document cannot be read when the sign-in starts.
        issuer.Down = HttpStatusCode.ServiceUnavailable;
        using var browser = app.Browser();
        Assert.Equal("/sign-in?provider_sign_in=unavailable", (await browser.StartAsync("/sign-in")).Landing());

        // Its token endpoint does not answer when the browser comes back.
        issuer.Down = null;
        var start = await browser.StartAsync("/sign-in");
        var comingBack = new Uri(issuer.Authorize(start.Headers.Location!, [])).PathAndQuery;
        issuer.TimesOut = true;
        Assert.Equal("/sign-in?provider_sign_in=unavailable", (await browser.GetAsync(comingBack)).Landing());
        Assert.Equal(0, app.Logs.Count("unhandled error"));
    }

    [Fact]
    public async Task Without_a_database_a_sign_in_still_starts_and_a_link_goes_back_to_the_app()
    {
        var issuer = new FakeIssuer();
        await using var app = issuer.CreateAppWithoutDatabase();
        using var browser = app.Browser();

        // Starting a sign-in asks nothing of the database: the browser is sent to the provider.
        var start = await browser.StartAsync("/sign-in");
        Assert.StartsWith(FakeIssuer.Issuer, start.Landing(), StringComparison.Ordinal);

        // A link needs to know who is signed in, which nothing can tell for now: back to the app, with
        // a word that says trying again may work, and never a page of JSON.
        Assert.Equal("/library?provider_sign_in=unavailable", (await browser.StartAsync("/library", link: true)).Landing());
        Assert.Equal(0, app.Logs.Count("unhandled error"));
    }

    [Fact]
    public async Task A_provider_that_answers_wrongly_is_said_and_never_an_error_page()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();

        // Its token endpoint answers a server error: trying again may work.
        var start = await browser.StartAsync("/sign-in");
        var comingBack = new Uri(issuer.Authorize(start.Headers.Location!, [])).PathAndQuery;
        issuer.TokenEndpointDown = HttpStatusCode.ServiceUnavailable;
        Assert.Equal("/sign-in?provider_sign_in=unavailable", (await browser.GetAsync(comingBack)).Landing());

        // Its discovery document is not one (a captive portal, a mistyped issuer): nothing could be done.
        var broken = new FakeIssuer { DiscoveryBody = "<html>not a document</html>" };
        await using var second = await broken.StartAppAsync();
        using var visitor = second.Browser();
        Assert.Equal("/sign-in?provider_sign_in=failed", (await visitor.StartAsync("/sign-in")).Landing());
        Assert.Equal(0, app.Logs.Count("unhandled error") + second.Logs.Count("unhandled error"));
    }

    [Fact]
    public async Task The_return_is_a_navigation_and_nothing_else()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        var start = await browser.StartAsync();
        var comingBack = new Uri(issuer.Authorize(start.Headers.Location!, []));

        // The same code and state, posted as a form: refused before the handler reads it.
        var posted = await browser.PostAsync(comingBack.AbsolutePath, new FormUrlEncodedContent(QueryHelpers.ParseQuery(comingBack.Query)
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToString())));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, posted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await browser.GetAsync("/api/v1/me").StatusAsync());

        // The navigation itself still goes through.
        Assert.Equal("/", (await browser.GetAsync(comingBack.PathAndQuery)).Landing());
    }

    [Fact]
    public async Task A_start_typed_or_from_an_old_browser_goes_through()
    {
        // No Sec-Fetch-Site: a browser that sends none, or an address typed. Only another site's page is refused.
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        var start = await browser.GetAsync(ProviderCalls.Start);
        Assert.Equal(HttpStatusCode.Found, start.StatusCode);
        Assert.StartsWith(FakeIssuer.Issuer + "/authorize?", start.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_a_screen_of_the_app_is_come_back_to()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        Assert.Equal("/", (await browser.SignInAsync(issuer, "https://example.org/")).Landing());
        Assert.Equal("/", (await browser.SignInAsync(issuer, "//example.org/")).Landing());
        Assert.Equal("/", (await browser.SignInAsync(issuer, "/api/v1/me")).Landing());
        // An outcome written in advance is dropped.
        Assert.Equal("/?provider_sign_in=linked", (await browser.LinkAsync(issuer, "/?provider_sign_in=linked_elsewhere")).Landing());
    }

    [Theory]
    [InlineData("cross-site")]
    [InlineData("same-site")]
    public async Task An_unknown_provider_and_a_start_from_another_site_go_nowhere_but_back_to_the_app(string from)
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var browser = app.Browser();
        Assert.Equal("/sign-in?provider_sign_in=failed", (await browser.StartAsync("/sign-in", provider: "nope")).Landing());

        // Another site's page, or a navigation that went through another site on its way (a reverse proxy's
        // own sign-in): not sent to the provider, and back in the app, where the visitor asks again.
        var request = new HttpRequestMessage(HttpMethod.Get, ProviderCalls.Start + "?return_to=%2Fsign-in");
        request.Headers.Add("Sec-Fetch-Site", from);
        Assert.Equal("/sign-in?provider_sign_in=failed", (await browser.SendAsync(request)).Landing());
        Assert.Equal(0, issuer.Requests);
    }

    [Fact]
    public async Task Without_providers_there_is_nothing_to_start()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        Assert.Empty((await browser.GetAsync("/api/v1/config").JsonAsync()).GetProperty("oidc_providers").EnumerateArray());
        Assert.Equal("/?provider_sign_in=failed", (await browser.StartAsync()).Landing());
    }

    [Fact]
    public async Task Behind_a_reverse_proxy_the_provider_sends_the_browser_back_to_the_public_address()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync(new() { ["RESPAWN_PUBLIC_URL"] = "https://respawn.example.org" });
        using var browser = app.Browser();
        var start = await browser.StartAsync();
        var asked = QueryHelpers.ParseQuery(start.Headers.Location!.Query);
        Assert.Equal("https://respawn.example.org/api/v1/auth/oidc/test/callback", asked["redirect_uri"]);
    }

    [Fact]
    public async Task The_keys_are_fetched_once_and_not_again_at_will()
    {
        var issuer = new FakeIssuer();
        await using var app = await issuer.StartAppAsync();
        using var first = app.Browser();
        await first.SignInAsync(issuer);
        var asked = issuer.Requests; // discovery, keys, the code
        await first.PostAsync("/api/v1/auth/logout", null);
        await first.SignInAsync(issuer);
        Assert.Equal(asked + 1, issuer.Requests); // the code only

        // The provider rotates its key. The first token naming a key the app does not know has the keys
        // fetched again, at once but in the background (Microsoft.IdentityModel's first refresh), and that
        // token is refused, not a failure of the app. The answer is held until then, or the fetch could
        // land while the token is still being validated and have it accepted.
        issuer.Rotate();
        issuer.HoldKeys();
        using var second = app.Browser();
        Assert.Equal("/?provider_sign_in=failed", (await second.SignInAsync(issuer)).Landing());
        issuer.ReleaseKeys();
        await issuer.KeyKnownAsync(app);
        Assert.Equal("/", (await second.SignInAsync(issuer)).Landing());

        // Another key a moment later: refused, and nothing is asked of the provider but the code. The keys
        // are fetched again once their minimum interval has passed, not at the call of whoever posts a
        // token naming an unknown key.
        issuer.Rotate();
        asked = issuer.Requests;
        using var third = app.Browser();
        Assert.Equal("/?provider_sign_in=failed", (await third.SignInAsync(issuer)).Landing());
        Assert.Equal(asked + 1, issuer.Requests);
        Assert.Equal(0, app.Logs.Count("unhandled error"));
    }
}
