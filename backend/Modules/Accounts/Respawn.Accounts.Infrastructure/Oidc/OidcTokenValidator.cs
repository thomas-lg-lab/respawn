using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Respawn.Accounts;
using Respawn.Accounts.Ports;

namespace Respawn.Accounts.Infrastructure.Oidc;

/// <summary>The timings of ID token validation. One instance for the app; a test may change them.</summary>
public sealed class OidcValidationSettings
{
    /// <summary>How long a provider's keys are used before they are fetched again.</summary>
    public TimeSpan CacheLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>A provider's keys are fetched again before the cache expires only for a token naming a
    /// key that is not in it (the provider rotated its keys), and never more often than this: callers
    /// are not signed in yet, and must not be able to make the app send requests at will.</summary>
    public TimeSpan MinRefreshInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>The provider's clock is not ours: a token is accepted this long after it expires.</summary>
    public TimeSpan Leeway { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Validation of an OpenID Connect ID token sent by the front.
///
/// Nothing is specific to a provider: its discovery document gives the signing keys. The token's
/// signature, issuer, audience and lifetime are checked by Microsoft.IdentityModel; only asymmetric
/// algorithms are accepted, so a provider's public key can never be used as a shared secret.
/// </summary>
internal sealed class OidcTokenValidator(
    IHttpClientFactory http,
    OidcValidationSettings settings,
    TimeProvider clock,
    ILogger<OidcTokenValidator> logger) : IExternalTokenValidator, IDisposable
{
    public const string HttpClientName = "oidc";

    private static readonly string[] Algorithms =
        [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.EcdsaSha256];

    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (long FetchedAt, OpenIdConnectConfiguration Keys)> _keys = [];
    private readonly Dictionary<string, long> _attempts = []; // provider name -> last fetch, successful or not

    public async Task<ExternalIdentity> ValidateAsync(
        OidcProvider provider, string idToken, string nonce, CancellationToken ct)
    {
        string? keyId;
        try
        {
            // Nothing is fetched for something that is not even shaped like a token.
            keyId = new JsonWebToken(idToken).Kid;
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException)
        {
            throw Refused();
        }

        var keys = await KeysAsync(provider, string.IsNullOrEmpty(keyId) ? null : keyId, ct);
        var result = await _handler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            // Providers disagree on the trailing slash of their issuer; both forms name the same one.
            ValidIssuers = [provider.Issuer, provider.Issuer + "/"],
            ValidAudience = provider.ClientId,
            IssuerSigningKeys = keys.SigningKeys,
            ValidAlgorithms = Algorithms,
            ClockSkew = settings.Leeway,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken token)
        {
            // Every refusal means the same thing to the caller; saying which check failed would tell
            // whoever posted the token how to get closer.
            logger.LogInformation("an ID token of {Provider} was refused: {Reason}", provider.Name, result.Exception?.GetType().Name);
            throw Refused();
        }

        if (string.IsNullOrWhiteSpace(token.Subject)
            || !token.TryGetPayloadValue<string>("nonce", out var sent)
            || !string.Equals(sent, nonce, StringComparison.Ordinal))
        {
            throw Refused();
        }

        // A token for several audiences says which client asked for it: it must be this one.
        if (token.Audiences.Skip(1).Any()
            && !(token.TryGetPayloadValue<string>("azp", out var party) && party == provider.ClientId))
        {
            throw Refused();
        }

        token.TryGetPayloadValue<string>("email", out var email);
        token.TryGetPayloadValue<string>("name", out var name);
        token.TryGetPayloadValue<string>("preferred_username", out var username);
        return new ExternalIdentity(
            Issuer: provider.Issuer,
            Subject: token.Subject,
            Email: string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
            EmailVerified: EmailVerified(token),
            DisplayName: string.IsNullOrWhiteSpace(name) ? (string.IsNullOrWhiteSpace(username) ? null : username) : name,
            ExpiresAt: new DateTimeOffset(DateTime.SpecifyKind(token.ValidTo, DateTimeKind.Utc)));
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>The claim is a JSON boolean; some providers send it as a string.</summary>
    private static bool EmailVerified(JsonWebToken token)
    {
        if (!token.TryGetPayloadValue<object>("email_verified", out var claim))
        {
            return false;
        }

        return claim is true || (claim is string text && bool.TryParse(text, out var verified) && verified);
    }

    /// <summary>The provider's keys, from the cache when it is fresh and holds the key the token names.</summary>
    private async Task<OpenIdConnectConfiguration> KeysAsync(OidcProvider provider, string? keyId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = clock.GetTimestamp();
            var cached = _keys.TryGetValue(provider.Name, out var entry);
            var fresh = cached && clock.GetElapsedTime(entry.FetchedAt, now) < settings.CacheLifetime;
            if (fresh && (keyId is null || entry.Keys.SigningKeys.Any(key => key.KeyId == keyId)))
            {
                return entry.Keys;
            }

            if (_attempts.TryGetValue(provider.Name, out var attempted)
                && clock.GetElapsedTime(attempted, now) < settings.MinRefreshInterval)
            {
                // Fetched a moment ago: the token names a key the provider does not have.
                return fresh ? entry.Keys : throw Unavailable();
            }

            _attempts[provider.Name] = now;
            OpenIdConnectConfiguration keys;
            try
            {
                // Not cancelled with the request: the keys are fetched for every caller, and a caller
                // who leaves must not leave the others waiting for the next allowed fetch. The client's
                // timeout bounds it, and that timeout is a cancellation too: caught here like any failure.
                keys = await FetchAsync(provider, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not AccountException)
            {
                logger.LogWarning("the keys of {Provider} could not be fetched: {Reason}", provider.Name, ex.GetType().Name);
                throw Unavailable();
            }

            _keys[provider.Name] = (now, keys);
            return keys;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<OpenIdConnectConfiguration> FetchAsync(OidcProvider provider, CancellationToken ct)
    {
        using var client = http.CreateClient(HttpClientName);
        var configuration = await OpenIdConnectConfigurationRetriever.GetAsync(
            $"{provider.Issuer}/.well-known/openid-configuration", new HttpsDocumentRetriever(client), ct);
        if (configuration.Issuer?.TrimEnd('/') != provider.Issuer)
        {
            logger.LogWarning("the discovery document of {Provider} names another issuer", provider.Name);
            throw Refused();
        }

        return configuration;
    }

    private static AccountException Refused() => new(AccountError.InvalidToken);

    private static AccountException Unavailable() => new(AccountError.ProviderUnavailable);

    /// <summary>One GET over HTTPS, no retry: how often a provider is asked is decided above.</summary>
    private sealed class HttpsDocumentRetriever(HttpClient client) : IDocumentRetriever
    {
        public async Task<string> GetDocumentAsync(string address, CancellationToken cancel)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new IOException("a provider's documents are only fetched over HTTPS");
            }

            using var response = await client.GetAsync(uri, cancel);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancel);
        }
    }
}
