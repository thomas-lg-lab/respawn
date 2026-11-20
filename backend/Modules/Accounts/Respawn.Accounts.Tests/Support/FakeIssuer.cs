using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Respawn.Accounts.Api;
using Respawn.Testing;

namespace Respawn.Accounts.Tests.Support;

/// <summary>
/// An OpenID Connect provider answering from memory: discovery, keys, the authorization step and the
/// token endpoint.
///
/// The app's handler talks to it as to a real provider: the tokens are really signed and the keys really
/// published, the code is redeemed with its PKCE verifier, and the ID token carries the nonce the app
/// asked for, so every check of the exchange is exercised; only the network is replaced. The visitor's
/// part at the provider is <see cref="Authorize"/>: it reads the request the browser was sent with and
/// answers the address the provider would send the browser back to.
/// </summary>
internal sealed class FakeIssuer
{
    public const string Issuer = "https://issuer.test";
    public const string ClientId = "respawn-test";

    private readonly ConcurrentDictionary<string, Grant> _grants = new();
    private RsaSecurityKey _key = NewKey("test-key");

    /// <summary>A key this provider never publishes, under the name of the one it does.</summary>
    public RsaSecurityKey OtherKey { get; } = NewKey("test-key");

    /// <summary>How many requests the app sent the provider.</summary>
    public int Requests { get; private set; }

    /// <summary>The forms the app posted to the token endpoint.</summary>
    public ConcurrentQueue<Dictionary<string, string>> Redemptions { get; } = new();

    /// <summary>When set, the provider answers this status to everything.</summary>
    public HttpStatusCode? Down { get; set; }

    /// <summary>When set, the provider never answers in time: the client's timeout fires.</summary>
    public bool TimesOut { get; set; }

    /// <summary>When set, the token endpoint alone answers this status.</summary>
    public HttpStatusCode? TokenEndpointDown { get; set; }

    /// <summary>When set, the discovery document is this text, answered with 200: not a document at all.</summary>
    public string? DiscoveryBody { get; set; }

    public HttpMessageHandler Handler => new Transport(this);

    /// <summary>RESPAWN_OIDC_PROVIDERS naming this provider, under the issuer given.</summary>
    public static string Providers(string issuer, string? label = null) =>
        JsonSerializer.Serialize(new[] { new { name = "test", label, issuer, client_id = ClientId } });

    /// <summary>The app with its database, configured with this provider and reaching it in memory.</summary>
    public Task<TestApp> StartAppAsync(Dictionary<string, string?>? settings = null)
    {
        var all = new Dictionary<string, string?> { ["RESPAWN_OIDC_PROVIDERS"] = Providers(Issuer) };
        foreach (var (key, value) in settings ?? [])
        {
            all[key] = value;
        }

        return TestApp.StartAsync(all, services =>
            services.AddHttpClient(ProviderSignIn.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Handler));
    }

    /// <summary>Sign with a new key from now on, as a provider does from time to time.</summary>
    public void Rotate() => _key = NewKey($"test-key-{Guid.NewGuid():N}");

    /// <summary>
    /// The visitor signs in at the provider, which sends the browser back: the address of the app it is
    /// sent to, with a code for an ID token of these claims. Every claim a check looks at can be
    /// overridden, or removed with a null; <paramref name="signedBy"/> signs it with another key.
    /// </summary>
    public string Authorize(Uri request, (string Claim, object? Value)[] claims, RsaSecurityKey? signedBy = null)
    {
        var asked = QueryHelpers.ParseQuery(request.Query);
        Assert.Equal(Issuer + "/authorize", request.GetLeftPart(UriPartial.Path));
        var code = Guid.NewGuid().ToString("N");
        _grants[code] = new Grant(
            asked["redirect_uri"].ToString(), asked["code_challenge"].ToString(), asked["nonce"].ToString(), claims, signedBy);
        return QueryHelpers.AddQueryString(asked["redirect_uri"].ToString(), new Dictionary<string, string?>
        {
            ["code"] = code,
            ["state"] = asked["state"].ToString(),
        });
    }

    /// <summary>The provider turns the visitor away: the address the browser is sent back to.</summary>
    public static string Deny(Uri request)
    {
        var asked = QueryHelpers.ParseQuery(request.Query);
        return QueryHelpers.AddQueryString(asked["redirect_uri"].ToString(), new Dictionary<string, string?>
        {
            ["error"] = "access_denied",
            ["state"] = asked["state"].ToString(),
        });
    }

    private string IdToken(Grant grant)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["aud"] = ClientId,
            ["sub"] = "subject-1",
            ["nonce"] = grant.Nonce,
            ["iat"] = now,
            ["exp"] = now + 300,
            ["email"] = "player@example.org",
            ["email_verified"] = true,
            ["name"] = "Player One",
        };
        foreach (var (claim, value) in grant.Claims)
        {
            if (value is null)
            {
                claims.Remove(claim);
            }
            else
            {
                claims[claim] = value;
            }
        }

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            SigningCredentials = new SigningCredentials(grant.SignedBy ?? _key, SecurityAlgorithms.RsaSha256),
        });
    }

    private static RsaSecurityKey NewKey(string id) => new(RSA.Create(2048)) { KeyId = id };

    private HttpResponseMessage Discovery() => Json(new
    {
        issuer = Issuer,
        authorization_endpoint = $"{Issuer}/authorize",
        token_endpoint = $"{Issuer}/token",
        jwks_uri = $"{Issuer}/jwks",
        response_types_supported = new[] { "code" },
        code_challenge_methods_supported = new[] { "S256" },
    });

    private HttpResponseMessage Keys()
    {
        var parameters = _key.Rsa.ExportParameters(includePrivateParameters: false);
        return Json(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    kid = _key.KeyId,
                    n = Base64UrlEncoder.Encode(parameters.Modulus),
                    e = Base64UrlEncoder.Encode(parameters.Exponent),
                },
            },
        });
    }

    /// <summary>The token endpoint: a code once, for the client it was given to, with its verifier.</summary>
    private HttpResponseMessage Redeem(Dictionary<string, string> form)
    {
        Redemptions.Enqueue(form);
        var verifier = form.GetValueOrDefault("code_verifier") ?? "";
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        if (form.GetValueOrDefault("grant_type") != "authorization_code"
            || form.GetValueOrDefault("client_id") != ClientId
            || !_grants.TryRemove(form.GetValueOrDefault("code") ?? "", out var grant)
            || grant.RedirectUri != form.GetValueOrDefault("redirect_uri")
            || grant.Challenge != challenge)
        {
            return Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest);
        }

        return Json(new { access_token = "an-access-token", token_type = "Bearer", expires_in = 300, id_token = IdToken(grant) });
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private sealed record Grant(
        string RedirectUri, string Challenge, string Nonce, (string Claim, object? Value)[] Claims, RsaSecurityKey? SignedBy);

    private sealed class Transport(FakeIssuer issuer) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            issuer.Requests++;
            if (issuer.TimesOut)
            {
                // What HttpClient throws when its timeout fires.
                throw new TaskCanceledException("the request timed out", new TimeoutException());
            }

            if (issuer.Down is { } status)
            {
                return new HttpResponseMessage(status);
            }

            if (request.RequestUri!.AbsolutePath == "/token" && issuer.TokenEndpointDown is { } tokenStatus)
            {
                return new HttpResponseMessage(tokenStatus);
            }

            return request.RequestUri!.AbsolutePath switch
            {
                "/.well-known/openid-configuration" when issuer.DiscoveryBody is { } body =>
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) },
                "/.well-known/openid-configuration" => issuer.Discovery(),
                "/jwks" => issuer.Keys(),
                "/token" when request.Method == HttpMethod.Post => issuer.Redeem(
                    QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct)).ToDictionary(pair => pair.Key, pair => pair.Value.ToString())),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }
    }
}

/// <summary>A browser signing in through the provider, as the front sends it.</summary>
internal static class ProviderCalls
{
    public const string Start = "/api/v1/auth/oidc/test";

    /// <summary>The navigation that starts a sign-in through the provider, or with <paramref name="link"/> a
    /// link to the signed-in account, from the app's own page.</summary>
    public static Task<HttpResponseMessage> StartAsync(
        this HttpClient browser, string? returnTo = "/", string provider = "test", bool link = false)
    {
        var address = $"/api/v1/auth/oidc/{provider}" + (link ? "/link" : "")
            + (returnTo is null ? "" : "?return_to=" + Uri.EscapeDataString(returnTo));
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        return browser.SendAsync(request);
    }

    /// <summary>Starts, signs in at the provider with these claims, and comes back: the app's answer to the
    /// return, which sends the browser on to the app.</summary>
    public static Task<HttpResponseMessage> SignInAsync(
        this HttpClient browser, FakeIssuer issuer, string returnTo = "/", params (string Claim, object? Value)[] claims) =>
        browser.ThroughProviderAsync(issuer, returnTo, link: false, claims);

    /// <summary>The same, as a link of the provider to the signed-in account.</summary>
    public static Task<HttpResponseMessage> LinkAsync(
        this HttpClient browser, FakeIssuer issuer, string returnTo = "/", params (string Claim, object? Value)[] claims) =>
        browser.ThroughProviderAsync(issuer, returnTo, link: true, claims);

    private static async Task<HttpResponseMessage> ThroughProviderAsync(
        this HttpClient browser, FakeIssuer issuer, string returnTo, bool link, (string Claim, object? Value)[] claims)
    {
        var start = await browser.StartAsync(returnTo, link: link);
        Assert.Equal(HttpStatusCode.Found, start.StatusCode);
        return await browser.GetAsync(new Uri(issuer.Authorize(start.Headers.Location!, claims)).PathAndQuery);
    }

    /// <summary>Where the app sent the browser on to.</summary>
    public static string Landing(this HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        return response.Headers.Location!.OriginalString;
    }
}
