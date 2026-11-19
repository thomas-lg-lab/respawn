using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Respawn.Testing;

namespace Respawn.Accounts.Tests;

/// <summary>The settings of the interface kept on the account: the language, the theme, the character.</summary>
public sealed class SettingsTests
{
    private const string Settings = "/api/v1/me/settings";

    [Fact]
    public async Task A_new_account_has_no_settings()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();

        var registered = await browser.RegisterAsync().JsonAsync();

        Assert.Equal(JsonValueKind.Null, registered.GetProperty("settings").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await browser.GetAsync("/api/v1/me").JsonAsync()).GetProperty("settings").ValueKind);
    }

    [Fact]
    public async Task Settings_survive_a_new_session()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();

        var saved = await browser.PutAsJsonAsync(Settings, new { language = "fr", theme = "dark", character = "plain" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        AssertSettings(("fr", "dark", "plain"), await saved.JsonAsync());

        await browser.PostAsync("/api/v1/auth/logout", null);
        using var elsewhere = app.Browser();
        var signedIn = await elsewhere.LoginAsync("first@example.org", TestApp.Password).JsonAsync();

        AssertSettings(("fr", "dark", "plain"), signedIn);
        AssertSettings(("fr", "dark", "plain"), await elsewhere.GetAsync("/api/v1/me").JsonAsync());
    }

    [Fact]
    public async Task Saving_again_replaces_the_three()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();
        await browser.PutAsJsonAsync(Settings, new { language = "fr", theme = "dark", character = "plain" });

        await browser.PutAsJsonAsync(Settings, new { language = "en", theme = "system", character = "pixel" });

        AssertSettings(("en", "system", "pixel"), await browser.GetAsync("/api/v1/me").JsonAsync());
    }

    [Fact]
    public async Task A_setting_missing_or_unknown_is_refused_and_nothing_changes()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();
        await browser.PutAsJsonAsync(Settings, new { language = "fr", theme = "dark", character = "plain" });

        Assert.Equal(
            (HttpStatusCode.UnprocessableEntity, "invalid_request"),
            await browser.PutAsJsonAsync(Settings, new { language = "de", theme = "light", character = "pixel" }).RefusalAsync());
        Assert.Equal(
            (HttpStatusCode.UnprocessableEntity, "invalid_request"),
            await browser.PutAsJsonAsync(Settings, new { language = "en", theme = "Light", character = "pixel" }).RefusalAsync());
        Assert.Equal(
            (HttpStatusCode.UnprocessableEntity, "invalid_request"),
            await browser.PutAsJsonAsync(Settings, new { language = "en", theme = "light" }).RefusalAsync());

        AssertSettings(("fr", "dark", "plain"), await browser.GetAsync("/api/v1/me").JsonAsync());
    }

    [Fact]
    public async Task Saving_needs_a_session()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();

        Assert.Equal(
            (HttpStatusCode.Unauthorized, "not_signed_in"),
            await browser.PutAsJsonAsync(Settings, new { language = "fr", theme = "dark", character = "plain" }).RefusalAsync());
    }

    private static void AssertSettings((string Language, string Theme, string Character) expected, JsonElement account)
    {
        var settings = account.GetProperty("settings");
        Assert.Equal(expected.Language, settings.GetProperty("language").GetString());
        Assert.Equal(expected.Theme, settings.GetProperty("theme").GetString());
        Assert.Equal(expected.Character, settings.GetProperty("character").GetString());
    }
}
