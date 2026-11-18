using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Api;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Games.Ports;
using Respawn.Games.Tests.Support;
using Respawn.Testing;

namespace Respawn.Games.Tests;

/// <summary>Linking a Steam account ("Sign in through Steam"), the linked accounts, and unlinking one.
/// Steam is never called: a test plays the browser coming back from it, and Steam's word on the answer.</summary>
public sealed class SteamLinkTests
{
    private const string SteamId = "76561190000000001";
    private const string Start = "/api/v1/linked-accounts/steam/link";

    [Fact]
    public async Task The_linked_accounts_and_the_link_need_a_session()
    {
        var steam = new FakeSteamSignIn();
        await using var app = await steam.StartAppAsync();
        using var browser = app.Browser();

        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await browser.GetAsync("/api/v1/linked-accounts").RefusalAsync());
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await browser.GetAsync(Start).RefusalAsync());
        Assert.Equal(
            (HttpStatusCode.Unauthorized, "not_signed_in"),
            await browser.DeleteAsync($"/api/v1/linked-accounts/{Guid.NewGuid()}").RefusalAsync());

        await browser.RegisterAsync();
        Assert.Empty(await AccountsAsync(browser));
    }

    [Fact]
    public async Task A_steam_account_proven_at_steam_is_linked_to_the_account_that_asked()
    {
        var steam = new FakeSteamSignIn();
        await using var app = await steam.StartAppAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();

        // The browser is sent to Steam's sign-in, told where to come back and for which site.
        var toSteam = await StartAsync(browser, "/library?sort=playtime");
        var asked = QueryHelpers.ParseQuery(toSteam.Query);
        Assert.Equal(FakeSteamSignIn.SignIn, toSteam.GetLeftPart(UriPartial.Path));
        Assert.Equal("checkid_setup", asked["openid.mode"]);
        Assert.Equal("http://localhost", asked["openid.realm"]);
        Assert.StartsWith("http://localhost" + Start + "/callback?state=", asked["openid.return_to"].ToString(), StringComparison.Ordinal);

        // Back from Steam: the answer is checked with Steam, then the account is linked.
        var back = await browser.GetAsync(FakeSteamSignIn.ComingBack(toSteam, FakeSteamSignIn.ClaimedId(SteamId)));

        Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);
        Assert.Equal("/library?sort=playtime&steam_link=linked", back.Headers.Location!.OriginalString);
        Assert.Equal("check_authentication", Assert.Single(steam.Asked)["openid.mode"]);
        Assert.Equal(FakeSteamSignIn.ClaimedId(SteamId), steam.Asked[0]["openid.claimed_id"]);

        var account = Assert.Single(await AccountsAsync(browser));
        Assert.Equal(("steam", SteamId), (account.GetProperty("source").GetString(), account.GetProperty("external_id").GetString()));
        Assert.Equal(JsonValueKind.Null, account.GetProperty("display_name").ValueKind);

        // No session is opened from Steam: the answer sets no cookie but the end of the exchange's own.
        Assert.DoesNotContain(back.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], cookie => cookie.StartsWith("respawn_session", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Linking_the_same_steam_account_again_changes_nothing()
    {
        var steam = new FakeSteamSignIn();
        await using var app = await steam.StartAppAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();

        Assert.Equal("/?steam_link=linked", await LinkAsync(browser, SteamId));
        Assert.Equal("/?steam_link=linked", await LinkAsync(browser, SteamId));

        Assert.Single(await AccountsAsync(browser));
    }

    [Fact]
    public async Task A_steam_account_has_one_owner()
    {
        var steam = new FakeSteamSignIn();
        await using var app = await steam.StartAppAsync();
        using var first = app.Browser();
        using var second = app.Browser();
        await first.RegisterAsync();
        OpenRegistration();
        await second.RegisterAsync("second@example.org", "Second");

        Assert.Equal("/?steam_link=linked", await LinkAsync(first, SteamId));
        Assert.Equal("/?steam_link=linked_elsewhere", await LinkAsync(second, SteamId));

        Assert.Empty(await AccountsAsync(second));
    }

    [Fact]
    public async Task An_answer_steam_does_not_confirm_links_nothing()
    {
        var steam = new FakeSteamSignIn { Confirms = false };
        await using var app = await steam.StartAppAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();

        Assert.Equal("/?steam_link=failed", await LinkAsync(browser, SteamId));

        Assert.Single(steam.Asked);
        Assert.Empty(await AccountsAsync(browser));
    }

    [Theory]
    [InlineData("https://steamcommunity.com/openid/id/7656119000000000")]
    [InlineData("https://steamcommunity.com/openid/id/765611900000000011")]
    [InlineData("https://steamcommunity.com/openid/id/76561190000000001/")]
    [InlineData("https://steamcommunity.com/openid/id/76561190000000001?x=1")]
    [InlineData("https://steamcommunity.com.example.org/openid/id/76561190000000001")]
    [InlineData("https://example.org/openid/id/76561190000000001")]
    [InlineData("https://steamcommunity.com/id/76561190000000001")]
    [InlineData("76561190000000001")]
    public async Task Only_an_identifier_of_steam_is_a_steam_account(string claimedId)
    {
        // Steam confirms the answer here: the shape of what it proves is still checked.
        var steam = new FakeSteamSignIn();
        await using var app = await steam.StartAppAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();

        var toSteam = await StartAsync(browser);
        var back = await browser.GetAsync(FakeSteamSignIn.ComingBack(toSteam, claimedId));

        Assert.Equal("/?steam_link=failed", back.Headers.Location!.OriginalString);
        Assert.Empty(await AccountsAsync(browser));
    }

    [Fact]
    public async Task A_visitor_who_gives_up_at_steam_comes_back_with_nothing_linked()
    {
        var steam = new FakeSteamSignIn();
        await using var app = await steam.StartAppAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();

        var toSteam = await StartAsync(browser, "/library");
        var back = await browser.GetAsync(FakeSteamSignIn.ComingBack(toSteam, FakeSteamSignIn.ClaimedId(SteamId), mode: "cancel"));

        Assert.Equal("/library?steam_link=failed", back.Headers.Location!.OriginalString);
        Assert.Empty(steam.Asked);
        Assert.Empty(await AccountsAsync(browser));
    }

    [Fact]
    public async Task An_answer_only_counts_in_the_browser_that_asked()
    {
        // Someone else's answer, pasted into another browser: the exchange was not started there.
        var steam = new FakeSteamSignIn();
        await using var app = await steam.StartAppAsync();
        using var asker = app.Browser();
        using var other = app.Browser();
        await asker.RegisterAsync();
        OpenRegistration();
        await other.RegisterAsync("second@example.org", "Second");

        var toSteam = await StartAsync(asker);
        var back = await other.GetAsync(FakeSteamSignIn.ComingBack(toSteam, FakeSteamSignIn.ClaimedId(SteamId)));

        Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);
        Assert.EndsWith("steam_link=failed", back.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Empty(await AccountsAsync(asker));
        Assert.Empty(await AccountsAsync(other));
    }

    [Fact]
    public async Task An_answer_with_a_state_that_is_not_the_apps_links_nothing()
    {
        var steam = new FakeSteamSignIn();
        await using var app = await steam.StartAppAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();

        var toSteam = await StartAsync(browser);
        var forged = new Uri(FakeSteamSignIn.ComingBack(toSteam, FakeSteamSignIn.ClaimedId(SteamId)));
        var state = QueryHelpers.ParseQuery(forged.Query)["state"].ToString();
        var back = await browser.GetAsync(forged.PathAndQuery.Replace(Uri.EscapeDataString(state), "forged", StringComparison.Ordinal));

        Assert.EndsWith("steam_link=failed", back.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Empty(await AccountsAsync(browser));
    }

    [Fact]
    public async Task The_account_that_comes_back_must_be_the_one_that_asked()
    {
        // In the same browser, another account signed in while the first was at Steam.
        var steam = new FakeSteamSignIn();
        await using var app = await steam.StartAppAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();
        var toSteam = await StartAsync(browser);

        await browser.PostAsync("/api/v1/auth/logout", null);
        OpenRegistration();
        await browser.RegisterAsync("second@example.org", "Second");
        var back = await browser.GetAsync(FakeSteamSignIn.ComingBack(toSteam, FakeSteamSignIn.ClaimedId(SteamId)));

        Assert.Equal("/?steam_link=failed", back.Headers.Location!.OriginalString);
        Assert.Empty(await AccountsAsync(browser));

        // And with nobody signed in at all.
        var again = await StartAsync(browser);
        await browser.PostAsync("/api/v1/auth/logout", null);
        back = await browser.GetAsync(FakeSteamSignIn.ComingBack(again, FakeSteamSignIn.ClaimedId(SteamId)));
        Assert.Equal("/?steam_link=failed", back.Headers.Location!.OriginalString);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<GamesDbContext>().LinkedAccounts.CountAsync());
    }

    [Fact]
    public async Task Steam_not_answering_sends_back_to_the_app_saying_so()
    {
        var steam = new FakeSteamSignIn { Unreachable = true };
        await using var app = await steam.StartAppAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();

        var answer = await browser.GetAsync(Start + "?return_to=/library");

        Assert.Equal(HttpStatusCode.Redirect, answer.StatusCode);
        Assert.Equal("/library?steam_link=unavailable", answer.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Behind_a_proxy_steam_sends_back_to_the_address_the_instance_is_reached_at()
    {
        var steam = new FakeSteamSignIn();
        await using var app = await steam.StartAppAsync(new() { ["RESPAWN_PUBLIC_URL"] = "https://respawn.example.org" });
        // The exchange's cookie is for HTTPS only there: this browser carries cookies by hand.
        using var browser = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        var session = Cookies(await browser.RegisterAsync());

        var start = await SendAsync(browser, Start, session);
        var toSteam = start.Headers.Location!;
        var asked = QueryHelpers.ParseQuery(toSteam.Query);
        Assert.Equal("https://respawn.example.org", asked["openid.realm"]);
        Assert.StartsWith("https://respawn.example.org" + Start + "/callback?state=", asked["openid.return_to"].ToString(), StringComparison.Ordinal);

        var comingBack = new Uri(FakeSteamSignIn.ComingBack(toSteam, FakeSteamSignIn.ClaimedId(SteamId)));
        var back = await SendAsync(browser, comingBack.PathAndQuery, session + "; " + Cookies(start));

        Assert.Equal("/?steam_link=linked", back.Headers.Location!.OriginalString);

        // Nothing else of the app takes the setting for its own address.
        Assert.Equal(HttpStatusCode.OK, await browser.GetAsync("/healthz").StatusAsync());
    }

    [Theory]
    [InlineData("/library", "/library")]
    [InlineData("/games/half-life.2?tab=stats#top", "/games/half-life.2?tab=stats#top")]
    [InlineData("/", "/")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("library", "/")]
    [InlineData("//example.org", "/")]
    [InlineData("/\\example.org", "/")]
    [InlineData("/a\\b", "/")]
    [InlineData("https://example.org/", "/")]
    [InlineData("/a\tb", "/")]
    [InlineData("/a\r\nSet-Cookie: x=1", "/")]
    [InlineData("/api/v1/auth/logout", "/")]
    [InlineData("/API/v1/me", "/")]
    public void Only_a_screen_of_the_app_is_come_back_to(string? asked, string expected) =>
        Assert.Equal(expected, SteamLink.ReturnPath(asked));

    [Fact]
    public async Task Unlinking_forgets_everything_read_from_the_account_and_keeps_the_catalogue()
    {
        var steam = new FakeSteamSignIn();
        await using var app = await steam.StartAppAsync();
        using var browser = app.Browser();
        using var other = app.Browser();
        await browser.RegisterAsync();
        OpenRegistration();
        await other.RegisterAsync("second@example.org", "Second");
        await LinkAsync(browser, SteamId);
        await LinkAsync(other, "76561190000000002");
        var mine = (await AccountsAsync(browser))[0].GetProperty("id").GetGuid();
        var theirs = (await AccountsAsync(other))[0].GetProperty("id").GetGuid();
        await RecordAsync(app, mine, ("620", "Portal 2", 120), ("400", "Portal", 30));
        await RecordAsync(app, mine, ("620", "Portal 2", 180));
        await RecordAsync(app, theirs, ("620", "Portal 2", 45));

        // Not mine, or not there: the same answer, and nothing is touched.
        Assert.Equal(
            (HttpStatusCode.NotFound, "unknown_linked_account"),
            await browser.DeleteAsync($"/api/v1/linked-accounts/{theirs}").RefusalAsync());
        Assert.Equal(
            (HttpStatusCode.NotFound, "unknown_linked_account"),
            await browser.DeleteAsync($"/api/v1/linked-accounts/{Guid.NewGuid()}").RefusalAsync());
        Assert.Equal(2, await TotalAsync(browser));

        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync($"/api/v1/linked-accounts/{mine}").StatusAsync());

        Assert.Empty(await AccountsAsync(browser));
        Assert.Equal(0, await TotalAsync(browser));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        // What was read from the other account is whole; the games are the instance's and stay.
        Assert.Equal(theirs, (await db.LinkedAccounts.SingleAsync()).Id);
        Assert.Equal(theirs, (await db.RawSnapshots.SingleAsync()).LinkedAccountId);
        Assert.Equal(theirs, (await db.Ownerships.SingleAsync()).LinkedAccountId);
        Assert.Equal(1, await db.Observations.CountAsync());
        Assert.Equal((2, 2), (await db.Games.CountAsync(), await db.StoreEntries.CountAsync()));
        Assert.Equal(1, await TotalAsync(other));

        // Linked again, it starts from nothing.
        Assert.Equal("/?steam_link=linked", await LinkAsync(browser, SteamId));
        Assert.NotEqual(mine, (await AccountsAsync(browser))[0].GetProperty("id").GetGuid());
        Assert.Equal(0, await TotalAsync(browser));
    }

    [Fact]
    public async Task The_description_says_how_accounts_are_linked_and_unlinked()
    {
        var steam = new FakeSteamSignIn();
        await using var app = await steam.StartAppAsync();
        using var browser = app.Browser();
        var paths = (await browser.GetAsync("/api/v1/openapi.json").JsonAsync()).GetProperty("paths");

        Assert.True(paths.GetProperty("/api/v1/linked-accounts").GetProperty("get").GetProperty("responses").TryGetProperty("200", out _));
        var unlink = paths.GetProperty("/api/v1/linked-accounts/{id}").GetProperty("delete").GetProperty("responses");
        Assert.Equal(["204", "401", "404"], unlink.EnumerateObject().Select(response => response.Name).Order());
        var link = paths.GetProperty(Start).GetProperty("get");
        Assert.True(link.GetProperty("responses").TryGetProperty("302", out _));
        Assert.Equal("return_to", Assert.Single(link.GetProperty("parameters").EnumerateArray()).GetProperty("name").GetString());
        // Where Steam sends the browser back is the exchange's, not a route of the contract.
        Assert.DoesNotContain(paths.EnumerateObject(), path => path.Name.Contains("callback", StringComparison.Ordinal));
    }

    /// <summary>Lets a second account be created: registration closes after the first.</summary>
    private static void OpenRegistration() =>
        TestDatabase.Run("INSERT INTO accounts.settings (key, value) VALUES ('registration_open', 'true') ON CONFLICT DO NOTHING");

    /// <summary>Starts a link and gives the address at Steam the browser is sent to.</summary>
    private static async Task<Uri> StartAsync(HttpClient browser, string? returnTo = null)
    {
        var answer = await browser.GetAsync(returnTo is null ? Start : Start + "?return_to=" + Uri.EscapeDataString(returnTo));
        Assert.Equal(HttpStatusCode.Redirect, answer.StatusCode);
        Assert.Equal("no-store", answer.Headers.CacheControl?.ToString());
        return answer.Headers.Location!;
    }

    /// <summary>A whole link, Steam claiming this account: where the browser ends.</summary>
    private static async Task<string> LinkAsync(HttpClient browser, string steamId)
    {
        var toSteam = await StartAsync(browser);
        var back = await browser.GetAsync(FakeSteamSignIn.ComingBack(toSteam, FakeSteamSignIn.ClaimedId(steamId)));
        Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);
        return back.Headers.Location!.OriginalString;
    }

    private static async Task<JsonElement[]> AccountsAsync(HttpClient browser) =>
        [.. (await browser.GetAsync("/api/v1/linked-accounts").JsonAsync()).GetProperty("accounts").EnumerateArray()];

    private static async Task<int> TotalAsync(HttpClient browser) =>
        (await browser.GetAsync("/api/v1/library").JsonAsync()).GetProperty("total").GetInt32();

    private static async Task RecordAsync(TestApp app, Guid account, params (string Id, string Name, int Minutes)[] items)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var reading = new LibraryReading(
            "owned-games",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            "{}",
            [.. items.Select(item => new OwnedItem(item.Id, item.Name, TimeSpan.FromMinutes(item.Minutes), null))]);
        await scope.ServiceProvider.GetRequiredService<ILibraryService>().RecordLibraryAsync(account, reading, default);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient browser, string address, string cookies)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Add("Cookie", cookies);
        return browser.SendAsync(request);
    }

    /// <summary>The cookies an answer sets, as a browser would send them back.</summary>
    private static string Cookies(HttpResponseMessage answer) =>
        string.Join("; ", answer.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0]));

    private static async Task<string> Cookies(Task<HttpResponseMessage> answer) => Cookies(await answer);
}
