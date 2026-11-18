using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        Assert.Contains("<rs-root", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // What is a file is what the build ships, not what an address looks like: a screen may end like
    // a file, and gets the document whatever asks for it. So the answer needs no word to a cache.
    [Theory]
    [InlineData("/games/archive.zip", "text/html,application/xhtml+xml,*/*;q=0.8")]
    [InlineData("/games/archive.zip", "*/*")]
    [InlineData("/users/someone@example.com", "*/*")]
    [InlineData("/theme.css", "text/css,*/*;q=0.1")]
    public async Task A_screen_that_ends_like_a_file_gets_the_document_whatever_asks(string path, string accept)
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Accept", accept);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<rs-root", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("'nonce-", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Empty(response.Headers.Vary);
    }

    // Where only the build puts files, nothing there is a missing file, to a browser that goes there
    // too: a script or a font of an older build gets a 404, never a page.
    [Theory]
    [InlineData("/main-MISSING0.js")]
    [InlineData("/styles-5INURTSO.css")]
    [InlineData("/media/inter-latin-wght-normal-NRMW37G5.woff2")]
    [InlineData("/media/anything")]
    public async Task A_file_of_the_build_that_is_missing_stays_missing(string path)
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Accept", "text/html");

        Assert.Equal((HttpStatusCode.NotFound, "http_error"), await client.SendAsync(request).RefusalAsync());
    }

    // A file the build ships is served as it is, to a browser that goes to it too.
    [Fact]
    public async Task A_file_asked_as_a_page_is_still_the_file()
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/favicon.svg");
        request.Headers.TryAddWithoutValidation("Accept", "text/html");

        var response = await client.SendAsync(request);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
    }

    // The front's components bring their styles as style elements, which the policy only lets through
    // with the nonce of the response: the document and its policy must name the same one.
    [Theory]
    [InlineData("/")]
    [InlineData("/sign-in")]
    [InlineData("/index.html")]
    public async Task The_document_carries_the_nonce_its_policy_allows(string path)
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();

        var response = await client.GetAsync(path);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        var nonce = Regex.Match(policy, "'nonce-([A-Za-z0-9+/=]+)'").Groups[1].Value;

        Assert.Equal($"default-src 'self'; style-src 'self' 'nonce-{nonce}'; frame-ancestors 'none'", policy);
        Assert.Equal(16, Convert.FromBase64String(nonce).Length);
        var document = await response.Content.ReadAsStringAsync();
        Assert.Contains($"ngCspNonce=\"{nonce}\"", document, StringComparison.Ordinal);
        Assert.DoesNotContain("RESPAWN_CSP_NONCE", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_nonce_is_never_used_twice_and_the_document_never_kept()
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();

        var first = await client.GetAsync("/");
        var second = await client.GetAsync("/");

        Assert.NotEqual(
            first.Headers.GetValues("Content-Security-Policy").Single(),
            second.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-store", first.Headers.CacheControl?.ToString());
        // Nothing a browser could ask "has it changed?" about: that answer would keep an old nonce.
        Assert.Null(first.Headers.ETag);
        Assert.Null(first.Content.Headers.LastModified);
    }

    [Fact]
    public async Task Asking_only_for_the_headers_of_the_document_gets_no_body()
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();

        using var request = new HttpRequestMessage(HttpMethod.Head, "/sign-in");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
        Assert.Contains("'nonce-", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    // Only the document needs the nonce: every other answer keeps the policy with no exception in it.
    [Theory]
    [InlineData("/healthz")]
    [InlineData("/favicon.svg")]
    [InlineData("/main-QX2ZHB7T.js")]
    [InlineData("/api/v1/openapi.json")]
    public async Task What_is_not_the_document_allows_no_style_element(string path)
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();

        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("default-src 'self'; frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
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

    // The page that draws the API's description: open, with no database, and with a policy of its own
    // that reaches nothing else.
    [Theory]
    [InlineData("/docs")]
    [InlineData("/docs/")]
    public async Task The_api_page_is_served_without_database(string path)
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();

        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("swagger-ui", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(
            "default-src 'self'; img-src 'self' data:; frame-ancestors 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single());
    }

    // The licence of what the page ships comes with it, under the name its library gives it: no
    // extension. It is there to be read; a file of a kind the app does not know is still not served.
    [Fact]
    public async Task The_licence_shipped_with_the_api_page_can_be_read()
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();

        var licence = await client.GetAsync("/docs/LICENSE");
        Assert.Equal(HttpStatusCode.OK, licence.StatusCode);
        Assert.Equal("text/plain", licence.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", licence.Content.Headers.ContentType?.CharSet);
        Assert.Equal("nosniff", licence.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("licence", await licence.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NotFound, await client.GetAsync("/docs/bundle.unknown").StatusAsync());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/sign-in")]
    [InlineData("/docs-of-the-front")]
    [InlineData("/healthz")]
    [InlineData("/api/v1/openapi.json")]
    public async Task The_policy_of_the_api_page_reaches_nothing_else(string path)
    {
        await using var app = TestApp.Offline();
        using var client = app.Browser();

        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("data:", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    // The document never stands in for an answer of the API, nor for a file that is not there.
    [Theory]
    [InlineData("GET", "/api/v1/nothing", HttpStatusCode.NotFound)]
    [InlineData("GET", "/api", HttpStatusCode.NotFound)]
    [InlineData("GET", "/healthz/nothing", HttpStatusCode.NotFound)]
    [InlineData("GET", "/main-MISSING0.js", HttpStatusCode.NotFound)]
    [InlineData("GET", "/docs/nothing", HttpStatusCode.NotFound)]
    [InlineData("GET", "/docs/nothing.js", HttpStatusCode.NotFound)]
    [InlineData("GET", "/api/v1/nothing.zip", HttpStatusCode.NotFound)]
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
        // The page that draws this description is not part of what it describes.
        Assert.DoesNotContain(paths.EnumerateObject(), path => path.Name.StartsWith("/docs", StringComparison.Ordinal));
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
