using System.Net;
using System.Text.Json;
using Respawn.Testing;

namespace Respawn.Tests;

/// <summary>What the runtime expects of the app whatever its state.</summary>
public sealed class ContractTests
{
    [Fact]
    public async Task Healthz_answers_without_database_or_secrets()
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();
        var body = await client.GetAsync("/healthz").JsonAsync();
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_page_is_served_without_database()
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("respawn", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.StartsWith("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, await client.GetAsync("/static/app.js").StatusAsync());
    }

    [Fact]
    public async Task Data_routes_answer_503_without_database()
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();
        Assert.Equal(
            (HttpStatusCode.ServiceUnavailable, "database_unavailable"),
            await client.GetAsync("/api/v1/config").RefusalAsync());

        // Someone who says they are signed in cannot be told otherwise without the database.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Add("Cookie", "respawn_session=anything");
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "database_unavailable"), await client.SendAsync(request).RefusalAsync());

        // Without a session there is nothing to look up: the answer does not wait for the database.
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await client.GetAsync("/api/v1/me").RefusalAsync());
    }

    [Fact]
    public async Task The_database_is_picked_up_when_its_file_appears()
    {
        TestDatabase.EnsureCreated();
        var file = Path.Combine(Directory.CreateTempSubdirectory("respawn-secrets-").FullName, "database-url");
        await using var app = TestApp.Offline(file);
        using var client = app.Browser();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await client.GetAsync("/api/v1/config").StatusAsync());

        // The plain URI a managed database hands out.
        await File.WriteAllTextAsync(file, TestDatabase.Url + "\n");
        Assert.Equal(HttpStatusCode.OK, await client.GetAsync("/api/v1/config").StatusAsync());
    }

    [Fact]
    public async Task A_forged_remote_user_header_gives_no_access()
    {
        await using var app = await TestApp.StartAsync();
        using var owner = app.Browser();
        await owner.RegisterAsync();

        using var stranger = app.Browser();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Add("Remote-User", "first@example.org");
        request.Headers.Add("Remote-Groups", "admins");
        Assert.Equal((HttpStatusCode.Unauthorized, "not_signed_in"), await stranger.SendAsync(request).RefusalAsync());
    }

    [Fact]
    public async Task The_openapi_description_is_published()
    {
        await using var app = await TestApp.StartAsync();
        using var client = app.Browser();
        var document = await client.GetAsync("/api/v1/openapi.json").JsonAsync();
        var paths = document.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/me", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/login", out _));
        Assert.False(paths.TryGetProperty("/healthz", out _));
    }

    // The front's types are generated from the description: it must say what the API really answers.
    [Fact]
    public async Task The_openapi_description_matches_what_the_api_answers()
    {
        await using var app = await TestApp.StartAsync();
        using var client = app.Browser();
        var document = await client.GetAsync("/api/v1/openapi.json").JsonAsync();

        // Fields are spelled as on the wire, in snake_case.
        var user = document.GetProperty("components").GetProperty("schemas").GetProperty("UserResponse").GetProperty("properties");
        Assert.True(user.TryGetProperty("display_name", out _));
        Assert.False(user.TryGetProperty("displayName", out _));

        // Every route that declares a refusal still describes a success: declaring one answer by hand
        // hides the one the framework would have inferred, and the front then gets no type for it.
        var withoutSuccess =
            from path in document.GetProperty("paths").EnumerateObject()
            from operation in path.Value.EnumerateObject()
            where operation.Value.ValueKind == JsonValueKind.Object && operation.Value.TryGetProperty("responses", out _)
            let statuses = operation.Value.GetProperty("responses").EnumerateObject().Select(response => response.Name).ToList()
            where statuses.Any(status => status[0] is '4' or '5') && !statuses.Any(status => status[0] == '2')
            select $"{operation.Name.ToUpperInvariant()} {path.Name}";
        Assert.Empty(withoutSuccess);

        // The same document wherever it is asked: no address of the host in it.
        Assert.False(document.TryGetProperty("servers", out var servers) && servers.GetArrayLength() > 0);
    }
}
