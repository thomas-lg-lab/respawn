using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Respawn.Tests.Support;

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

    public HttpMessageHandler Handler => new Transport(this);

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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            issuer.Requests++;
            var path = request.RequestUri!.AbsolutePath;
            if (issuer.Down is { } status)
            {
                return Task.FromResult(new HttpResponseMessage(status));
            }

            if (path is not ("/.well-known/openid-configuration" or "/jwks"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(issuer.Answer(path), Encoding.UTF8, "application/json"),
            });
        }
    }
}
