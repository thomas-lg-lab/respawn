using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Accounts.Infrastructure.Identity;
using Respawn.Accounts.Infrastructure.Persistence;
using Respawn.Testing;

namespace Respawn.Accounts.Tests;

/// <summary>Local accounts: registration, sign-in, lockout, sign-out.</summary>
public sealed class AccountsTests
{
    private const string Login = "/api/v1/auth/login";

    [Fact]
    public async Task The_first_account_is_administrator_and_signed_in()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        var config = await browser.GetAsync("/api/v1/config").JsonAsync();
        Assert.True(config.GetProperty("registration_open").GetBoolean());
        Assert.True(config.GetProperty("first_account").GetBoolean());
        Assert.Equal(12, config.GetProperty("password_min_length").GetInt32());

        var response = await browser.RegisterAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True((await response.JsonAsync()).GetProperty("is_admin").GetBoolean());
        Assert.Contains("httponly", string.Join(';', response.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
        var me = await browser.GetAsync("/api/v1/me").JsonAsync();
        Assert.Equal("first@example.org", me.GetProperty("email").GetString());
        Assert.Equal("First", me.GetProperty("display_name").GetString());
    }

    [Fact]
    public async Task Registration_closes_after_the_first_account()
    {
        await using var app = await TestApp.StartAsync();
        using var first = app.Browser();
        await first.RegisterAsync();

        using var second = app.Browser();
        var config = await second.GetAsync("/api/v1/config").JsonAsync();
        Assert.False(config.GetProperty("registration_open").GetBoolean());
        Assert.False(config.GetProperty("first_account").GetBoolean());
        Assert.Equal(
            (HttpStatusCode.Forbidden, "registration_closed"),
            await second.RegisterAsync("second@example.org", "Second").RefusalAsync());
    }

    [Fact]
    public async Task A_weak_password_and_a_bad_email_are_refused()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        const string register = "/api/v1/auth/register";

        var weak = browser.PostAsJsonAsync(register, new { email = "a@example.org", display_name = "A", password = "short" });
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "weak_password"), await weak.RefusalAsync());

        var bad = browser.PostAsJsonAsync(register, new { email = "not-an-email", display_name = "A", password = TestApp.Password });
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "invalid_email"), await bad.RefusalAsync());

        var missing = await browser.PostAsJsonAsync(register, new { email = "a@example.org" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        var error = (await missing.JsonAsync()).GetProperty("error");
        Assert.Equal("invalid_request", error.GetProperty("code").GetString());
        Assert.Equal("Check these fields: display_name, password.", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Login_and_logout()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();
        Assert.Equal(HttpStatusCode.NoContent, await browser.PostAsync("/api/v1/auth/logout", null).StatusAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, await browser.GetAsync("/api/v1/me").StatusAsync());

        Assert.Equal(
            (HttpStatusCode.Unauthorized, "invalid_credentials"),
            await browser.LoginAsync("first@example.org", "wrong password").RefusalAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, await browser.LoginAsync("nobody@example.org", TestApp.Password).StatusAsync());

        Assert.Equal(HttpStatusCode.OK, await browser.LoginAsync(" First@Example.org ", TestApp.Password).StatusAsync());
        Assert.Equal(HttpStatusCode.OK, await browser.GetAsync("/api/v1/me").StatusAsync());
    }

    [Fact]
    public async Task A_session_ends_at_its_age()
    {
        var clock = new Clock();
        await using var app = await TestApp.StartAsync(
            new() { ["RESPAWN_SESSION_DAYS"] = "2" },
            services => services.AddSingleton<TimeProvider>(clock));
        using var browser = app.Browser();
        await browser.RegisterAsync();

        clock.Now += TimeSpan.FromDays(2) - TimeSpan.FromMinutes(1);
        Assert.Equal(HttpStatusCode.OK, await browser.GetAsync("/api/v1/me").StatusAsync());

        // Using it did not make it last longer.
        clock.Now += TimeSpan.FromMinutes(2);
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await browser.GetAsync("/api/v1/me").RefusalAsync());
    }

    [Fact]
    public async Task An_e_mail_already_used_is_refused()
    {
        await using var app = await TestApp.StartAsync();
        using var first = app.Browser();
        await first.RegisterAsync();
        TestDatabase.Run("INSERT INTO accounts.settings (key, value) VALUES ('registration_open', 'true')");

        using var second = app.Browser();
        Assert.Equal(
            (HttpStatusCode.Conflict, "account_exists"),
            await second.RegisterAsync(" First@Example.org ", "Second").RefusalAsync());
    }

    [Fact]
    public async Task Local_sign_in_can_be_turned_off()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();
        TestDatabase.Run("INSERT INTO accounts.settings (key, value) VALUES ('local_sign_in_enabled', 'false')");

        using var other = app.Browser();
        Assert.False((await other.GetAsync("/api/v1/config").JsonAsync()).GetProperty("local_sign_in").GetBoolean());
        Assert.Equal(
            (HttpStatusCode.Forbidden, "local_sign_in_disabled"),
            await other.LoginAsync("first@example.org", TestApp.Password).RefusalAsync());
        // The session opened before stays.
        Assert.Equal(HttpStatusCode.OK, await browser.GetAsync("/api/v1/me").StatusAsync());
    }

    [Theory]
    [InlineData("text/plain", "{}", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("application/json", "{ not json", HttpStatusCode.UnprocessableEntity)]
    public async Task A_body_that_is_not_the_one_expected_is_refused_in_the_format_of_the_api(
        string type, string body, HttpStatusCode status)
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, type);

        var answer = await browser.PostAsync("/api/v1/auth/login", content);

        Assert.Equal(status, answer.StatusCode);
        var error = (await answer.JsonAsync()).GetProperty("error");
        Assert.False(string.IsNullOrEmpty(error.GetProperty("code").GetString()));
        Assert.False(string.IsNullOrEmpty(error.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account()
    {
        await using var app = await TestApp.StartAsync();
        using var owner = app.Browser();
        await owner.RegisterAsync();

        using var browser = app.Browser();
        for (var attempt = 0; attempt < 3; attempt++) // the lockout threshold of the test settings
        {
            await browser.LoginAsync("first@example.org", "wrong password");
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await browser.LoginAsync("first@example.org", TestApp.Password).StatusAsync());
    }

    [Fact]
    public async Task A_locked_account_is_verified_like_any_other()
    {
        // The password is checked even when the account is locked: the answer must not come faster.
        var verified = new List<string>();
        await using var app = await TestApp.StartAsync(services: services =>
            services.AddScoped<IPasswordHasher<AppUser>>(_ => new RecordingHasher(verified)));
        using var owner = app.Browser();
        await owner.RegisterAsync();
        using var browser = app.Browser();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await browser.LoginAsync("first@example.org", "wrong password");
        }

        verified.Clear();
        Assert.Equal(
            (HttpStatusCode.Unauthorized, "invalid_credentials"),
            await browser.LoginAsync("first@example.org", TestApp.Password).RefusalAsync());
        Assert.Equal([TestApp.Password], verified);
    }

    [Fact]
    public async Task A_session_ends_with_its_cookie()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        var registered = await browser.RegisterAsync();
        var cookie = registered.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        Assert.StartsWith("respawn_session=", cookie, StringComparison.Ordinal);
        await browser.PostAsync("/api/v1/auth/logout", null);

        // The cookie as it was before signing out, sent again by someone who kept it.
        using var replay = app.Browser();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, await replay.SendAsync(request).StatusAsync());
    }

    [Fact]
    public async Task A_sign_in_closes_the_session_the_browser_carried()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        var registered = await browser.RegisterAsync();
        var carried = registered.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        // The same browser signs in again: it gets a new session, and the one it carried is closed.
        var signedIn = await browser.LoginAsync("first@example.org", TestApp.Password);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.NotEqual(carried, signedIn.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK, await browser.GetAsync("/api/v1/me").StatusAsync());

        using var replay = app.Browser();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Add("Cookie", carried);
        Assert.Equal(HttpStatusCode.Unauthorized, await replay.SendAsync(request).StatusAsync());
    }

    [Fact]
    public async Task Passwords_are_hashed_a_few_at_a_time()
    {
        // Each hash takes tens of megabytes: whatever asks, no more than a few run together.
        var running = 0;
        var most = 0;
        using var entered = new SemaphoreSlim(0);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = Enumerable.Range(0, PasswordWork.AtOnce + 3)
            .Select(_ => PasswordWork.RunAsync(
                async () =>
                {
                    var now = Interlocked.Increment(ref running);
                    lock (release)
                    {
                        most = Math.Max(most, now);
                    }

                    entered.Release();
                    await release.Task;
                    Interlocked.Decrement(ref running);
                    return true;
                },
                CancellationToken.None))
            .ToArray();

        for (var turn = 0; turn < PasswordWork.AtOnce; turn++)
        {
            Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None));
        }

        Assert.False(await entered.WaitAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None));
        release.SetResult();
        await Task.WhenAll(work);
        Assert.Equal(PasswordWork.AtOnce, most);
    }

    [Fact]
    public async Task A_request_from_another_site_is_refused()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        Assert.Equal(
            (HttpStatusCode.Forbidden, "cross_site_request"),
            await browser.LoginAsync("first@example.org", TestApp.Password, ("Origin", "https://elsewhere.example.org")).RefusalAsync());
    }

    [Fact]
    public async Task The_browser_says_where_a_request_comes_from()
    {
        // Sec-Fetch-Site decides when the browser sends it, whatever the Host header became on the way.
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        Task<HttpStatusCode> Code(params (string, string)[] headers) =>
            browser.LoginAsync("nobody@example.org", TestApp.Password, headers).StatusAsync();
        (string, string)[] rewritten = [("Origin", "https://games.example.org"), ("Host", "app.internal:8080")];

        // 401: went through the guard, refused for its credentials only.
        Assert.Equal(HttpStatusCode.Unauthorized, await Code([.. rewritten, ("Sec-Fetch-Site", "same-origin")]));
        Assert.Equal(HttpStatusCode.Unauthorized, await Code(("Sec-Fetch-Site", "none")));
        foreach (var elsewhere in new[] { "cross-site", "same-site", "anything-else" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, await Code([.. rewritten, ("Sec-Fetch-Site", elsewhere)]));
        }

        // An Origin matching the Host does not make up for a browser saying the request is cross-site.
        Assert.Equal(HttpStatusCode.Forbidden, await Code(("Origin", "http://localhost"), ("Sec-Fetch-Site", "cross-site")));
        // No Sec-Fetch-Site (an older browser): the Origin is compared with the Host.
        Assert.Equal(HttpStatusCode.Forbidden, await Code(rewritten));
        Assert.Equal(HttpStatusCode.Unauthorized, await Code(("Origin", "http://localhost")));
        // Neither header: not a browser page (a script, a command-line client).
        Assert.Equal(HttpStatusCode.Unauthorized, await Code());
        // A forwarded host is no proof of anything: it is not read.
        Assert.Equal(HttpStatusCode.Forbidden, await Code([.. rewritten, ("X-Forwarded-Host", "games.example.org")]));
    }

    [Fact]
    public async Task The_sign_in_routes_are_rate_limited()
    {
        await using var app = await TestApp.StartAsync(new() { ["RESPAWN_AUTH_RATE_LIMIT"] = "2/minute" });
        using var browser = app.Browser();
        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests],
            await LoginCodesAsync(browser, [null, null, null]));
        Assert.Equal(
            (HttpStatusCode.TooManyRequests, "too_many_requests"),
            await browser.LoginAsync("x@example.org", TestApp.Password).RefusalAsync());
    }

    [Fact]
    public async Task A_forged_forwarded_address_does_not_dodge_the_rate_limit()
    {
        // The peer is not a listed proxy (the default): what it says about the client's address is ignored.
        await using var app = await TestApp.StartAsync(new() { ["RESPAWN_AUTH_RATE_LIMIT"] = "2/minute" });
        using var browser = app.Browser();
        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests],
            await LoginCodesAsync(browser, ["203.0.113.1", "203.0.113.2", "203.0.113.3"]));
    }

    [Fact]
    public async Task Clients_behind_a_trusted_proxy_are_limited_one_by_one()
    {
        // With the proxy listed in RESPAWN_TRUSTED_PROXIES, each visitor has its own count.
        await using var app = await TestApp.StartAsync(new()
        {
            ["RESPAWN_AUTH_RATE_LIMIT"] = "2/minute",
            ["RESPAWN_TRUSTED_PROXIES"] = "198.51.100.0/24",
        });
        app.Peer = IPAddress.Parse("198.51.100.1");
        using var browser = app.Browser();
        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized],
            await LoginCodesAsync(browser, ["203.0.113.1", "203.0.113.1", "203.0.113.2"]));
        Assert.Equal(
            [HttpStatusCode.TooManyRequests, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests],
            await LoginCodesAsync(browser, ["203.0.113.1", "203.0.113.2", "203.0.113.2"]));
        // A visitor adding addresses of its own in front of the one the proxy appends stays in its bucket.
        Assert.Equal([HttpStatusCode.TooManyRequests], await LoginCodesAsync(browser, ["198.51.100.7, 203.0.113.1"]));
    }

    [Fact]
    public async Task A_proxy_that_is_not_listed_is_reported_once()
    {
        // Behind a proxy the app was not told about, the shared rate limit must not stay silent.
        const string warning = "RESPAWN_TRUSTED_PROXIES does not list";
        await using var app = await TestApp.StartAsync(new() { ["RESPAWN_TRUSTED_PROXIES"] = "198.51.100.1" });
        using var browser = app.Browser();

        // A listed proxy, then no proxy at all: nothing to report.
        app.Peer = IPAddress.Parse("198.51.100.1");
        await LoginCodesAsync(browser, ["203.0.113.1"]);
        app.Peer = IPAddress.Parse("203.0.113.200");
        await LoginCodesAsync(browser, [null]);
        Assert.Equal(0, app.Logs.Count(warning));

        app.Peer = IPAddress.Parse("198.51.100.9");
        await LoginCodesAsync(browser, ["203.0.113.1", "203.0.113.2", "198.51.100.7, 203.0.113.3"]);
        Assert.Equal(1, app.Logs.Count(warning));
    }

    [Fact]
    public async Task An_unknown_route_answers_in_the_error_format()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        Assert.Equal((HttpStatusCode.NotFound, "http_error"), await browser.GetAsync("/api/v1/nothing-here").RefusalAsync());
    }

    private static async Task<HttpStatusCode[]> LoginCodesAsync(HttpClient browser, string?[] forwardedFor)
    {
        var codes = new List<HttpStatusCode>();
        foreach (var address in forwardedFor)
        {
            (string, string)[] headers = address is null ? [] : [("X-Forwarded-For", address)];
            codes.Add(await browser.LoginAsync("x@example.org", TestApp.Password, headers).StatusAsync());
        }

        return [.. codes];
    }

    /// <summary>The real hasher, noting each password it is asked to verify.</summary>
    private sealed class RecordingHasher(List<string> verified) : IPasswordHasher<AppUser>
    {
        private readonly Argon2PasswordHasher _hasher = new();

        public string HashPassword(AppUser user, string password) => _hasher.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(AppUser user, string hashedPassword, string providedPassword)
        {
            verified.Add(providedPassword);
            return _hasher.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }

    /// <summary>A clock a test moves.</summary>
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
