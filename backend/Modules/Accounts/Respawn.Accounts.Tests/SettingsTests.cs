using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Accounts.Infrastructure.Persistence;
using Respawn.Accounts.Ports;
using Respawn.Accounts.Services;
using Respawn.Testing;

namespace Respawn.Accounts.Tests;

/// <summary>The settings of the interface kept on the account: the language, the theme, the character and
/// the home's layout.</summary>
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

        var saved = await browser.PutAsJsonAsync(
            Settings, new { language = "fr", theme = "dark", character = "plain", home = "column" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        AssertSettings(("fr", "dark", "plain", "column"), await saved.JsonAsync());

        await browser.PostAsync("/api/v1/auth/logout", null);
        using var elsewhere = app.Browser();
        var signedIn = await elsewhere.LoginAsync("first@example.org", TestApp.Password).JsonAsync();

        AssertSettings(("fr", "dark", "plain", "column"), signedIn);
        AssertSettings(("fr", "dark", "plain", "column"), await elsewhere.GetAsync("/api/v1/me").JsonAsync());
    }

    [Fact]
    public async Task Saving_again_replaces_the_four()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();
        await browser.PutAsJsonAsync(Settings, new { language = "fr", theme = "dark", character = "plain", home = "column" });

        await browser.PutAsJsonAsync(Settings, new { language = "en", theme = "system", character = "pixel", home = "blocks" });

        AssertSettings(("en", "system", "pixel", "blocks"), await browser.GetAsync("/api/v1/me").JsonAsync());
    }

    [Fact]
    public async Task A_home_layout_left_out_is_kept_and_is_blocks_for_a_first_save()
    {
        // A client that knows the first three settings only keeps working, and does not undo the fourth.
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();

        var first = await browser.PutAsJsonAsync(Settings, new { language = "fr", theme = "dark", character = "plain" });
        AssertSettings(("fr", "dark", "plain", "blocks"), await first.JsonAsync());

        await browser.PutAsJsonAsync(Settings, new { language = "fr", theme = "dark", character = "plain", home = "column" });
        var again = await browser.PutAsJsonAsync(Settings, new { language = "en", theme = "light", character = "pixel" });

        AssertSettings(("en", "light", "pixel", "column"), await again.JsonAsync());
        AssertSettings(("en", "light", "pixel", "column"), await browser.GetAsync("/api/v1/me").JsonAsync());
    }

    [Fact]
    public async Task A_setting_missing_or_unknown_is_refused_and_nothing_changes()
    {
        await using var app = await TestApp.StartAsync();
        using var browser = app.Browser();
        await browser.RegisterAsync();
        await browser.PutAsJsonAsync(Settings, new { language = "fr", theme = "dark", character = "plain", home = "column" });

        Assert.Equal(
            (HttpStatusCode.UnprocessableEntity, "invalid_request"),
            await browser.PutAsJsonAsync(Settings, new { language = "de", theme = "light", character = "pixel" }).RefusalAsync());
        Assert.Equal(
            (HttpStatusCode.UnprocessableEntity, "invalid_request"),
            await browser.PutAsJsonAsync(Settings, new { language = "en", theme = "Light", character = "pixel" }).RefusalAsync());
        Assert.Equal(
            (HttpStatusCode.UnprocessableEntity, "invalid_request"),
            await browser.PutAsJsonAsync(Settings, new { language = "en", theme = "light" }).RefusalAsync());
        Assert.Equal(
            (HttpStatusCode.UnprocessableEntity, "invalid_request"),
            await browser.PutAsJsonAsync(
                Settings, new { language = "en", theme = "light", character = "pixel", home = "grid" }).RefusalAsync());

        AssertSettings(("fr", "dark", "plain", "column"), await browser.GetAsync("/api/v1/me").JsonAsync());
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

    [Fact]
    public async Task The_migration_gives_the_accounts_that_kept_settings_the_blocks()
    {
        // A database as it was before the home's layout, with an account that kept its three settings and
        // one that never chose any.
        TestDatabase.EnsureCreated();
        var name = TestDatabase.Name[..^"_test".Length] + "_home_upgrade_test";
        var url = TestDatabase.UrlFor(name);
        TestDatabase.Admin($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
        TestDatabase.Create(url);

        await using var dataSource = TestDatabase.DataSource(url);
        var options = new DbContextOptionsBuilder<AccountsDbContext>();
        AccountsDbContext.Configure(options, dataSource);
        await using var db = new AccountsDbContext(options.Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("ProviderSignInByTheApi");

        // Written as that schema knows them, not through today's model.
        var chose = Guid.CreateVersion7();
        var never = Guid.CreateVersion7();
        foreach (var (id, isAdmin) in new[] { (chose, true), (never, false) })
        {
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO accounts.users
                    (id, display_name, is_admin, created_at, email_confirmed, phone_number_confirmed,
                     two_factor_enabled, lockout_enabled, access_failed_count)
                VALUES ({id}, 'Before the layout', {isAdmin}, {DateTimeOffset.UtcNow}, FALSE, FALSE, FALSE, TRUE, 0)
                """);
        }

        await db.Database.ExecuteSqlAsync(
            $"UPDATE accounts.users SET interface_language = 'fr', interface_theme = 'dark', interface_character = 'plain' WHERE id = {chose}");

        await migrator.MigrateAsync();

        var accounts = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id);
        Assert.Equal(
            new InterfaceSettings(Language.French, Theme.Dark, Character.Plain, HomeLayout.Blocks),
            accounts[chose].ToAccount().Settings);
        Assert.Null(accounts[never].ToAccount().Settings);
        Assert.Null(accounts[never].InterfaceHome);
    }

    private static void AssertSettings(
        (string Language, string Theme, string Character, string Home) expected, JsonElement account)
    {
        var settings = account.GetProperty("settings");
        Assert.Equal(expected.Language, settings.GetProperty("language").GetString());
        Assert.Equal(expected.Theme, settings.GetProperty("theme").GetString());
        Assert.Equal(expected.Character, settings.GetProperty("character").GetString());
        Assert.Equal(expected.Home, settings.GetProperty("home").GetString());
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
