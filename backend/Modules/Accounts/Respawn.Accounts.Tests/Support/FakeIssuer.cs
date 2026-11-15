using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Respawn.Accounts.Infrastructure.Oidc;
using Respawn.Testing;

namespace Respawn.Accounts.Tests.Support;

/// <summary>
/// An OpenID Connect provider answering from memory: discovery, keys, and tokens signed on demand.
///
/// The tokens are really signed and the keys really published, so every check of the validator
/// (signature, issuer, audience, lifetime) is exercised; only the network is replaced.
/// </summary>
internal sealed class FakeIssuer
{
    public const string Issuer = "https://issuer.test";
    public const string ClientId = "respawn-test";
    public const string Nonce = "nonce-123456";

    private RsaSecurityKey _key = NewKey("test-key");

    /// <summary>A key this provider never publishes, under the name of the one it does.</summary>
    public RsaSecurityKey OtherKey { get; } = NewKey("test-key");

    /// <summary>How many documents were fetched from the provider.</summary>
    public int Requests { get; private set; }

    /// <summary>When set, the provider answers this status to everything.</summary>
    public HttpStatusCode? Down { get; set; }

    /// <summary>When set, the provider never answers in time: the client's timeout fires.</summary>
    public bool TimesOut { get; set; }

    /// <summary>When set, the provider answers only once this completes.</summary>
    public TaskCompletionSource? Held { get; set; }

    public HttpMessageHandler Handler => new Transport(this);

    /// <summary>RESPAWN_OIDC_PROVIDERS naming this provider, under the issuer given.</summary>
    public static string Providers(string issuer) =>
        JsonSerializer.Serialize(new[] { new { name = "test", issuer, client_id = ClientId } });

    /// <summary>The app with its database, configured with this provider and reaching it in memory.</summary>
    public Task<TestApp> StartAppAsync(Dictionary<string, string?>? settings = null)
    {
        var all = new Dictionary<string, string?> { ["RESPAWN_OIDC_PROVIDERS"] = Providers(Issuer) };
        foreach (var (key, value) in settings ?? [])
        {
            all[key] = value;
        }

        return TestApp.StartAsync(all, services =>
            services.AddHttpClient(OidcTokenValidator.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Handler));
    }

    /// <summary>Sign with a new key from now on, as a provider does from time to time.</summary>
    public void Rotate() => _key = NewKey($"test-key-{Guid.NewGuid():N}");

    /// <summary>Mints an ID token. Every claim a check looks at can be overridden, or removed with a
    /// null, so a test can bend exactly one of them and watch the token be refused.</summary>
    public string Token(params (string Claim, object? Value)[] overrides) => TokenSignedBy(_key, overrides);

    public string TokenSignedBy(RsaSecurityKey key, params (string Claim, object? Value)[] overrides)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["aud"] = ClientId,
            ["sub"] = "subject-1",
            ["nonce"] = Nonce,
            ["iat"] = now,
            ["exp"] = now + 300,
            ["email"] = "player@example.org",
            ["email_verified"] = true,
            ["name"] = "Player One",
        };
        foreach (var (claim, value) in overrides)
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
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        });
    }

    private static RsaSecurityKey NewKey(string id) => new(RSA.Create(2048)) { KeyId = id };

    private string Answer(string path)
    {
        if (path == "/.well-known/openid-configuration")
        {
            return JsonSerializer.Serialize(new { issuer = Issuer, jwks_uri = $"{Issuer}/jwks" });
        }

        var parameters = _key.Rsa.ExportParameters(includePrivateParameters: false);
        return JsonSerializer.Serialize(new
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

    private sealed class Transport(FakeIssuer issuer) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            issuer.Requests++;
            var path = request.RequestUri!.AbsolutePath;
            if (issuer.TimesOut)
            {
                // What HttpClient throws when its timeout fires.
                throw new TaskCanceledException("the request timed out", new TimeoutException());
            }

            if (issuer.Held is { } held)
            {
                await held.Task.WaitAsync(ct);
            }

            if (issuer.Down is { } status)
            {
                return new HttpResponseMessage(status);
            }

            if (path is not ("/.well-known/openid-configuration" or "/jwks"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(issuer.Answer(path), Encoding.UTF8, "application/json"),
            };
        }
    }
}

internal static class ProviderCalls
{
    public static Task<HttpResponseMessage> ExchangeAsync(
        this HttpClient client, string token, string nonce = FakeIssuer.Nonce, string provider = "test") =>
        client.PostAsJsonAsync("/api/v1/auth/oidc", new { provider, id_token = token, nonce });
}
