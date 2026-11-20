using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Accounts.Infrastructure.Persistence;
using Respawn.Accounts.Ports;
using Respawn.Accounts.Services;
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

    [Fact]
    public async Task An_account_gone_during_the_request_is_no_longer_signed_in()
    {
        // The session is checked, then the account is deleted before its settings are saved.
        await using var app = await TestApp.StartAsync(services: services =>
        {
            services.AddScoped<AccountService>();
            services.AddScoped<IAccountService>(provider => new GoneBeforeSaving(
                provider.GetRequiredService<AccountService>(), provider.GetRequiredService<AccountsDbContext>()));
        });
        using var first = app.Browser();
        await first.RegisterAsync();
        // A member that owns nothing: the first account owns the default group, which keeps its owner.
        TestDatabase.Run("INSERT INTO accounts.settings (key, value) VALUES ('registration_open', 'true')");
        using var browser = app.Browser();
        var id = (await browser.RegisterAsync("second@example.org", "Second").JsonAsync()).GetProperty("id").GetGuid();

        Assert.Equal(
            (HttpStatusCode.Unauthorized, "not_signed_in"),
            await browser.PutAsJsonAsync(Settings, new { language = "fr", theme = "dark", character = "plain" }).RefusalAsync());

        using var scope = app.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Users.AnyAsync(u => u.Id == id));
    }

    private static void AssertSettings((string Language, string Theme, string Character) expected, JsonElement account)
    {
        var settings = account.GetProperty("settings");
        Assert.Equal(expected.Language, settings.GetProperty("language").GetString());
        Assert.Equal(expected.Theme, settings.GetProperty("theme").GetString());
        Assert.Equal(expected.Character, settings.GetProperty("character").GetString());
    }

    /// <summary>The account service, but the account is deleted right before its settings are saved, as
    /// if it went away while the request was on its way.</summary>
    private sealed class GoneBeforeSaving(AccountService inner, AccountsDbContext db) : IAccountService
    {
        public Task<SignInOptions> GetSignInOptionsAsync(CancellationToken ct) => inner.GetSignInOptionsAsync(ct);

        public Task<SignedIn> RegisterAsync(string email, string displayName, string password, CancellationToken ct) =>
            inner.RegisterAsync(email, displayName, password, ct);

        public Task<SignedIn> LoginAsync(string email, string password, CancellationToken ct) =>
            inner.LoginAsync(email, password, ct);

        public Task<SignedIn> SignInWithProviderAsync(OidcProvider provider, ExternalIdentity identity, CancellationToken ct) =>
            inner.SignInWithProviderAsync(provider, identity, ct);

        public Task LinkProviderAsync(Guid accountId, ExternalIdentity identity, CancellationToken ct) =>
            inner.LinkProviderAsync(accountId, identity, ct);

        public Task<IReadOnlyList<AccountProvider>> GetProvidersAsync(Guid accountId, CancellationToken ct) =>
            inner.GetProvidersAsync(accountId, ct);

        public Task<Account?> FindSignedInAsync(string? sessionToken, CancellationToken ct) =>
            inner.FindSignedInAsync(sessionToken, ct);

        public async Task<Account?> SaveSettingsAsync(Guid accountId, InterfaceSettings settings, CancellationToken ct)
        {
            await db.Database.ExecuteSqlAsync($"DELETE FROM accounts.users WHERE id = {accountId}", ct);
            return await inner.SaveSettingsAsync(accountId, settings, ct);
        }

        public Task LogoutAsync(string? sessionToken, CancellationToken ct) => inner.LogoutAsync(sessionToken, ct);
    }
}
