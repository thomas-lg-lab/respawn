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
    }

    // The front routes its own addresses: each gets the document, and the front shows the screen.
    [Theory]
    [InlineData("/sign-in")]
    [InlineData("/any/address/of/the/front")]
    [InlineData("/games/half-life.2")]
    [InlineData("/users/j.doe")]
    public async Task An_address_of_the_front_gets_its_document(string path)
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<rs-root>", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("/main-QX2ZHB7T.js", "public, max-age=31536000, immutable")]
    [InlineData("/chunk-f7g-vKq6.js", "public, max-age=31536000, immutable")]
    [InlineData("/theme.js", "no-cache")]
    [InlineData("/user-settings.js", "no-cache")]
    [InlineData("/favicon.svg", "no-cache")]
    public async Task A_file_of_the_front_is_kept_only_when_named_after_its_content(string path, string caching)
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();

        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(caching, response.Headers.CacheControl?.ToString());
        Assert.StartsWith("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    // The document never stands in for an answer of the API, nor for a file that is not there.
    [Theory]
    [InlineData("GET", "/api/v1/nothing", HttpStatusCode.NotFound)]
    [InlineData("GET", "/api", HttpStatusCode.NotFound)]
    [InlineData("GET", "/healthz/nothing", HttpStatusCode.NotFound)]
    [InlineData("GET", "/main-MISSING0.js", HttpStatusCode.NotFound)]
    [InlineData("GET", "/api/v1/auth/login", HttpStatusCode.MethodNotAllowed)]
    [InlineData("POST", "/api/v1/me", HttpStatusCode.MethodNotAllowed)]
    [InlineData("POST", "/sign-in", HttpStatusCode.NotFound)]
    public async Task What_is_not_the_front_keeps_its_own_answer(string method, string path, HttpStatusCode status)
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        Assert.Equal((status, "http_error"), await client.SendAsync(request).RefusalAsync());
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

        // A number is described as a number, never as "a number or a string".
        Assert.DoesNotContain("\"string\"", NumberTypes(document));

        // The same document wherever it is asked: no address of the host in it.
        Assert.False(document.TryGetProperty("servers", out var servers) && servers.GetArrayLength() > 0);
    }

    /// <summary>Every "type" of the description that is a list naming a number, as written.</summary>
    private static string NumberTypes(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => string.Concat(element.EnumerateObject().Select(property =>
                property.Name == "type" && property.Value.ValueKind == JsonValueKind.Array
                    && property.Value.EnumerateArray().Any(type => type.GetString() is "integer" or "number")
                    ? property.Value.GetRawText()
                    : NumberTypes(property.Value))),
            JsonValueKind.Array => string.Concat(element.EnumerateArray().Select(NumberTypes)),
            _ => "",
        };
}
