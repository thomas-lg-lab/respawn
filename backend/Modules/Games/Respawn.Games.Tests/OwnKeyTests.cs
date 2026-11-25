using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Respawn.Games.Api;
using Respawn.Games.Infrastructure.Credentials;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Games.Ports;
using Respawn.Games.Tests.Support;
using Respawn.Testing;

namespace Respawn.Games.Tests;

/// <summary>An owner's own Steam Web API key on their linked account: kept encrypted, never answered,
/// and the only key the sync reads that account with once it is given. Steam answers from memory.</summary>
public sealed class OwnKeyTests
{
    private const string SteamId = "76561190000000001";

    /// <summary>A key in the shape of Steam's, other than the instance's. Nobody's.</summary>
    private const string OwnKey = "FEDCBA9876543210FEDCBA9876543210";

    /// <summary>An encryption key for the tests: 32 bytes in base64. Nobody's.</summary>
    private static readonly string Encryption = Convert.ToBase64String([.. Enumerable.Range(1, 32).Select(i => (byte)i)]);

    /// <summary>Another one, for an instance whose encryption key changed. Nobody's.</summary>
    private static readonly string OtherEncryption = Convert.ToBase64String([.. Enumerable.Range(101, 32).Select(i => (byte)i)]);

    [Fact]
    public async Task A_key_given_on_the_account_reads_it_and_is_never_answered()
    {
        var steam = new FakeSteam();
        await using var app = await StartAsync(steam);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        Assert.False(await HasOwnKeyAsync(browser));

        Assert.Equal(HttpStatusCode.NoContent, await browser.PutAsJsonAsync($"/api/v1/linked-accounts/{account}/key", new { key = $" {OwnKey} " }).StatusAsync());

        var listed = await browser.GetStringAsync("/api/v1/linked-accounts");
        Assert.True(await HasOwnKeyAsync(browser));
        Assert.DoesNotContain(OwnKey, listed, StringComparison.OrdinalIgnoreCase);

        var run = await SyncNowAsync(browser, account);
        Assert.Equal(("succeeded", true), (run.GetProperty("outcome").GetString(), run.GetProperty("own_key").GetBoolean()));
        Assert.Equal([OwnKey], steam.Requests.Select(request => request.Key));

        // Stored sealed: neither the key nor its bytes are in the row, and nothing of it in the log.
        await using var scope = app.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<GamesDbContext>().LinkedAccounts.AsNoTracking().SingleAsync();
        Assert.NotNull(row.Credential);
        Assert.NotNull(row.CredentialSetAt);
        Assert.DoesNotContain(OwnKey, Encoding.Latin1.GetString(row.Credential), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(OwnKey, app.Logs.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_key_steam_refuses_fails_visibly_and_the_instance_key_is_never_tried()
    {
        var steam = new FakeSteam { Status = HttpStatusCode.Forbidden };
        await using var app = await StartAsync(steam);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        await SetKeyAsync(browser, account);

        var run = await SyncNowAsync(browser, account);

        Assert.Equal(
            ("failed", "refused", true),
            (run.GetProperty("outcome").GetString(), run.GetProperty("error").GetString(), run.GetProperty("own_key").GetBoolean()));
        Assert.Equal([OwnKey], steam.Requests.Select(request => request.Key));
        await using var scope = app.Services.CreateAsyncScope();
        var kept = await scope.ServiceProvider.GetRequiredService<GamesDbContext>().SyncRuns.AsNoTracking().SingleAsync();
        Assert.Equal("Steam refused the key given on this account (403)", kept.Detail);
        Assert.True(kept.OwnCredential);

        // The schedule says the same, and does not fall back either.
        Assert.Equal(1, await TurnAsync(app));
        Assert.Equal([OwnKey, OwnKey], steam.Requests.Select(request => request.Key));
    }

    [Fact]
    public async Task The_instance_key_refused_is_said_to_be_the_instances()
    {
        var steam = new FakeSteam { Status = HttpStatusCode.Unauthorized };
        await using var app = await StartAsync(steam);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        var run = await SyncNowAsync(browser, account);

        Assert.Equal(("refused", false), (run.GetProperty("error").GetString(), run.GetProperty("own_key").GetBoolean()));
        await using var scope = app.Services.CreateAsyncScope();
        var kept = await scope.ServiceProvider.GetRequiredService<GamesDbContext>().SyncRuns.AsNoTracking().SingleAsync();
        Assert.Equal("Steam refused the instance's key (401)", kept.Detail);
    }

    [Fact]
    public async Task A_key_taken_away_leaves_the_instance_key()
    {
        var (steam, clock) = (new FakeSteam(), new TestClock());
        await using var app = await StartAsync(steam, clock: clock);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        await SetKeyAsync(browser, account);
        await SyncNowAsync(browser, account);

        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync($"/api/v1/linked-accounts/{account}/key").StatusAsync());
        Assert.False(await HasOwnKeyAsync(browser));
        // Taking away what is not there changes nothing.
        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync($"/api/v1/linked-accounts/{account}/key").StatusAsync());

        clock.Advance(SyncSchedule.ManualPause);
        var run = await SyncNowAsync(browser, account);
        Assert.False(run.GetProperty("own_key").GetBoolean());
        Assert.Equal([OwnKey, FakeSteam.Key], steam.Requests.Select(request => request.Key));

        await using var scope = app.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<GamesDbContext>().LinkedAccounts.AsNoTracking().SingleAsync();
        Assert.Null(row.Credential);
        Assert.Null(row.CredentialSetAt);
    }

    [Fact]
    public async Task An_instance_without_a_steam_key_reads_with_the_owners()
    {
        var steam = new FakeSteam();
        var nowhere = Path.Combine(Path.GetTempPath(), $"respawn-no-steam-key-{Guid.NewGuid():N}");
        await using var app = await StartAsync(steam, new() { ["RESPAWN_STEAM_API_KEY"] = null, ["RESPAWN_STEAM_API_KEY_FILE"] = nowhere });
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        await SetKeyAsync(browser, account);

        var run = await SyncNowAsync(browser, account);

        Assert.Equal("succeeded", run.GetProperty("outcome").GetString());
        Assert.Equal([OwnKey], steam.Requests.Select(request => request.Key));
    }

    [Fact]
    public async Task A_key_the_instance_can_no_longer_open_fails_and_steam_is_not_asked()
    {
        var steam = new FakeSteam();
        var file = Path.Combine(Path.GetTempPath(), $"respawn-encryption-key-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(file, Encryption);
        await using var app = await StartAsync(steam, new() { ["RESPAWN_ENCRYPTION_KEY"] = null, ["RESPAWN_ENCRYPTION_KEY_FILE"] = file });
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        await SetKeyAsync(browser, account);

        // The instance's encryption key changes: what was sealed with the old one stays shut.
        await File.WriteAllTextAsync(file, OtherEncryption);
        JsonElement run;
        try
        {
            run = await SyncNowAsync(browser, account);
        }
        finally
        {
            File.Delete(file);
        }


        Assert.Equal(
            ("failed", "not_configured", true),
            (run.GetProperty("outcome").GetString(), run.GetProperty("error").GetString(), run.GetProperty("own_key").GetBoolean()));
        Assert.Empty(steam.Requests);
        await using var scope = app.Services.CreateAsyncScope();
        var kept = await scope.ServiceProvider.GetRequiredService<GamesDbContext>().SyncRuns.AsNoTracking().SingleAsync();
        Assert.Equal("the key given on this account cannot be read: the instance's encryption key is not the one it was kept with", kept.Detail);
    }

    [Fact]
    public async Task Without_an_encryption_key_nobody_can_give_a_key()
    {
        var steam = new FakeSteam();
        var nowhere = Path.Combine(Path.GetTempPath(), $"respawn-no-encryption-key-{Guid.NewGuid():N}");
        await using var app = await StartAsync(steam, new() { ["RESPAWN_ENCRYPTION_KEY"] = null, ["RESPAWN_ENCRYPTION_KEY_FILE"] = nowhere });
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        Assert.Equal(
            (HttpStatusCode.ServiceUnavailable, "keys_unavailable"),
            await browser.PutAsJsonAsync($"/api/v1/linked-accounts/{account}/key", new { key = OwnKey }).RefusalAsync());
        Assert.False(await HasOwnKeyAsync(browser));

        // And the instance's key still reads the account.
        Assert.Equal("succeeded", (await SyncNowAsync(browser, account)).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task An_encryption_key_given_in_a_file_is_used()
    {
        var steam = new FakeSteam();
        var file = Path.Combine(Path.GetTempPath(), $"respawn-encryption-key-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(file, Encryption + "\n");
        try
        {
            await using var app = await StartAsync(steam, new() { ["RESPAWN_ENCRYPTION_KEY"] = null, ["RESPAWN_ENCRYPTION_KEY_FILE"] = file });
            using var browser = app.Browser();
            var account = await LinkAsync(app, browser);
            await SetKeyAsync(browser, account);
            Assert.Equal("succeeded", (await SyncNowAsync(browser, account)).GetProperty("outcome").GetString());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("not a key", HttpStatusCode.BadRequest, "invalid_key")]
    [InlineData("key-with-dashes", HttpStatusCode.BadRequest, "invalid_key")]
    // Nothing but spaces is no key at all: the request itself is refused.
    [InlineData("   ", HttpStatusCode.UnprocessableEntity, "invalid_request")]
    public async Task What_is_not_a_steam_key_is_refused_before_it_is_kept(string key, HttpStatusCode status, string code)
    {
        var steam = new FakeSteam();
        await using var app = await StartAsync(steam);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        var refusal = await browser.PutAsJsonAsync($"/api/v1/linked-accounts/{account}/key", new { key }).RefusalAsync();

        Assert.Equal((status, code), refusal);
        Assert.False(await HasOwnKeyAsync(browser));
        Assert.Empty(steam.Requests);
    }

    [Fact]
    public async Task The_yearly_reviews_are_read_with_the_owner_s_key_too()
    {
        var steam = new FakeSteam();
        await using var app = await StartAsync(steam);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        await SetKeyAsync(browser, account);
        Assert.Equal("succeeded", (await SyncNowAsync(browser, account)).GetProperty("outcome").GetString());

        Assert.Equal(1, await Assert.Single(app.Services.GetServices<IHostedService>().OfType<YearReviewScheduler>()).RunDueAsync(default));

        // The owner's key and that one only, as for the library: never the instance's beside it.
        var reviews = steam.Requests.Where(request => request.Address.AbsolutePath.Contains("YearInReview", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(reviews);
        Assert.All(reviews, request => Assert.Equal(OwnKey, request.Key));
    }

    [Fact]
    public async Task Only_the_owner_gives_or_takes_away_a_key()
    {
        var steam = new FakeSteam();
        await using var app = await StartAsync(steam);
        using var owner = app.Browser();
        using var other = app.Browser();
        using var nobody = app.Browser();
        var account = await LinkAsync(app, owner);
        await SetKeyAsync(owner, account);
        OpenRegistration();
        await LinkAsync(app, other, "76561190000000002", "second@example.org");

        Assert.Equal(
            (HttpStatusCode.NotFound, "unknown_linked_account"),
            await other.PutAsJsonAsync($"/api/v1/linked-accounts/{account}/key", new { key = FakeSteam.Key }).RefusalAsync());
        Assert.Equal((HttpStatusCode.NotFound, "unknown_linked_account"), await other.DeleteAsync($"/api/v1/linked-accounts/{account}/key").RefusalAsync());
        Assert.Equal(
            (HttpStatusCode.NotFound, "unknown_linked_account"),
            await owner.PutAsJsonAsync($"/api/v1/linked-accounts/{Guid.NewGuid()}/key", new { key = OwnKey }).RefusalAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, await nobody.PutAsJsonAsync($"/api/v1/linked-accounts/{account}/key", new { key = OwnKey }).StatusAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, await nobody.DeleteAsync($"/api/v1/linked-accounts/{account}/key").StatusAsync());

        // The owner's key is still theirs.
        Assert.True(await HasOwnKeyAsync(owner));
        await SyncNowAsync(owner, account);
        Assert.Equal([OwnKey], steam.Requests.Select(request => request.Key));
    }

    [Fact]
    public async Task Giving_and_taking_away_keys_is_rate_limited()
    {
        var steam = new FakeSteam();
        await using var app = await StartAsync(steam, new() { ["RESPAWN_AUTH_RATE_LIMIT"] = "2/minute" });
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);

        await SetKeyAsync(browser, account);
        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync($"/api/v1/linked-accounts/{account}/key").StatusAsync());
        Assert.Equal(
            (HttpStatusCode.TooManyRequests, "too_many_requests"),
            await browser.PutAsJsonAsync($"/api/v1/linked-accounts/{account}/key", new { key = OwnKey }).RefusalAsync());
        Assert.False(await HasOwnKeyAsync(browser));

        // A bucket of its own: the other routes of the account still answer.
        Assert.Equal("succeeded", (await SyncNowAsync(browser, account)).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task A_key_goes_with_its_account_when_it_is_unlinked()
    {
        var steam = new FakeSteam();
        await using var app = await StartAsync(steam);
        using var browser = app.Browser();
        var account = await LinkAsync(app, browser);
        await SetKeyAsync(browser, account);

        Assert.Equal(HttpStatusCode.NoContent, await browser.DeleteAsync($"/api/v1/linked-accounts/{account}").StatusAsync());

        // Linked again: a new account, read with the instance's key.
        var again = await LinkAsync(app, browser, register: false);
        Assert.False(await HasOwnKeyAsync(browser));
        Assert.False((await SyncNowAsync(browser, again)).GetProperty("own_key").GetBoolean());
        Assert.Equal([FakeSteam.Key], steam.Requests.Select(request => request.Key));
    }

    [Fact]
    public void A_sealed_key_opens_only_for_its_account_and_with_its_encryption_key()
    {
        var encryption = new EncryptionOptions { Key = Encryption, KeyFile = "" };
        var cipher = new CredentialCipher(() => encryption);
        var (one, two) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var kept = cipher.Seal(one, OwnKey);

        Assert.Equal(OwnKey, cipher.Open(one, kept));
        // A new nonce each time: the same key sealed twice is never the same bytes.
        Assert.NotEqual(kept, cipher.Seal(one, OwnKey));
        Assert.Throws<CredentialException>(() => cipher.Open(two, kept));
        var other = new CredentialCipher(() => new EncryptionOptions { Key = OtherEncryption, KeyFile = "" });
        Assert.Throws<CredentialException>(() => other.Open(one, kept));
        var tampered = kept.ToArray();
        tampered[^1] ^= 1;
        Assert.Throws<CredentialException>(() => cipher.Open(one, tampered));
        Assert.Throws<CredentialException>(() => cipher.Open(one, [2, .. kept[1..]]));
        Assert.Throws<CredentialException>(() => new CredentialCipher(() => new EncryptionOptions { Key = "c2hvcnQ=", KeyFile = "" }).Seal(one, OwnKey));
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")] // 32 zeros
    [InlineData("QUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUE=")] // "A" 32 times
    [InlineData("Y2hhbmdlbWVjaGFuZ2VtZWNoYW5nZW1lY2hhbmdlbWU=")] // "changeme" 4 times
    public void A_placeholder_is_not_taken_as_an_encryption_key(string placeholder)
    {
        var error = Assert.Throws<CredentialException>(() => new EncryptionOptions { Key = placeholder, KeyFile = "" }.Resolve());
        Assert.Contains("not random", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(placeholder, error.Message, StringComparison.Ordinal);
    }

    private static Task<TestApp> StartAsync(FakeSteam steam, Dictionary<string, string?>? settings = null, TestClock? clock = null)
    {
        var all = new Dictionary<string, string?> { ["RESPAWN_ENCRYPTION_KEY"] = Encryption };
        foreach (var (key, value) in settings ?? [])
        {
            all[key] = value;
        }

        return steam.StartAppAsync(all, services =>
        {
            if (clock is not null)
            {
                services.AddSingleton<TimeProvider>(clock);
            }
        });
    }

    private static async Task<Guid> LinkAsync(
        TestApp app, HttpClient browser, string steamId = SteamId, string email = "first@example.org", bool register = true)
    {
        var owner = register
            ? (await browser.RegisterAsync(email).JsonAsync()).GetProperty("id").GetGuid()
            : (await browser.GetAsync("/api/v1/me").JsonAsync()).GetProperty("id").GetGuid();
        await using var scope = app.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<ILibraryService>().LinkAccountAsync(owner, "steam", steamId, null, default)).Id;
    }

    private static async Task SetKeyAsync(HttpClient browser, Guid account) =>
        Assert.Equal(HttpStatusCode.NoContent, await browser.PutAsJsonAsync($"/api/v1/linked-accounts/{account}/key", new { key = OwnKey }).StatusAsync());

    private static async Task<bool> HasOwnKeyAsync(HttpClient browser) =>
        (await browser.GetAsync("/api/v1/linked-accounts").JsonAsync()).GetProperty("accounts")[0].GetProperty("has_own_key").GetBoolean();

    private static async Task<JsonElement> SyncNowAsync(HttpClient browser, Guid account)
    {
        var answer = await browser.PostAsync($"/api/v1/linked-accounts/{account}/sync", null);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        return await answer.JsonAsync();
    }

    private static Task<int> TurnAsync(TestApp app) =>
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<SyncScheduler>()).RunDueAsync(default);

    private static void OpenRegistration() =>
        TestDatabase.Run("INSERT INTO accounts.settings (key, value) VALUES ('registration_open', 'true') ON CONFLICT DO NOTHING");
}
