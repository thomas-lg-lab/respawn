using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Respawn.Accounts.Ports;
using Respawn.SharedKernel;

namespace Respawn.Accounts.Api;

/// <summary>
/// Signing in through an OpenID Connect provider, done by the API: the browser is sent to the provider
/// and comes back to the API, which opens the session. The front only navigates; no token of the
/// provider ever reaches it.
///
/// The exchange (the authorization request with PKCE, the state and the nonce, the redemption of the
/// code, the validation of the ID token against the provider's published keys) is the work of ASP.NET
/// Core's OpenID Connect handler; what is written here is what the app does with someone a provider has
/// proven. The instance is a public client of each provider: no client secret.
///
/// Each configured provider is an authentication scheme of its own, added when its routes are first
/// asked for (<see cref="ProviderSignInPipeline.UseProviderSchemes"/>), since the providers are read from
/// the settings when first needed, not at start-up. The state is protected with the keys of ASP.NET
/// Core Data Protection, kept in memory: a sign-in started before a restart is started again.
/// </summary>
internal static class ProviderSignIn
{
    /// <summary>The parameter the front finds on the address it is sent back to, with the outcome.</summary>
    public const string OutcomeParameter = "provider_sign_in";

    /// <summary>The account a link was started by, kept in the state the provider hands back.</summary>
    public const string LinkItem = "respawn.link";

    /// <summary>The HTTP client the handler asks the providers with: their discovery document, their keys,
    /// and the redemption of a code.</summary>
    public const string HttpClientName = "oidc";

    private const string SchemePrefix = "oidc-";

    /// <summary>The signatures an ID token may carry: asymmetric only, so no key of the provider is
    /// ever a shared secret.</summary>
    private static readonly string[] Algorithms =
    [
        SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512,
    ];

    public static string Scheme(string provider) => SchemePrefix + provider;

    /// <summary>Where the browser is sent to the provider.</summary>
    public static string StartPath(string provider) => $"{AccountsApi.ProviderSignInPath}/{provider}";

    /// <summary>Where the provider sends the browser back.</summary>
    public static string CallbackPath(string provider) => StartPath(provider) + "/callback";

    public static OidcProvider? Find(AccountOptions options, string? scheme) =>
        scheme is not null && scheme.StartsWith(SchemePrefix, StringComparison.Ordinal)
            ? options.OidcProviders.FirstOrDefault(provider => provider.Name == scheme[SchemePrefix.Length..])
            : null;

    public static void Configure(OpenIdConnectOptions oidc, OidcProvider provider, HttpClient backchannel)
    {
        oidc.Authority = provider.Issuer;
        oidc.ClientId = provider.ClientId;
        oidc.ClientSecret = null;
        oidc.Backchannel = backchannel;
        oidc.DisableTelemetry = true;

        // The authorization code flow with PKCE, answered in the address of a plain navigation: a Lax
        // cookie follows it, where a form posted from the provider's site would carry none.
        oidc.ResponseType = OpenIdConnectResponseType.Code;
        oidc.ResponseMode = OpenIdConnectResponseMode.Query;
        oidc.UsePkce = true;
        oidc.Scope.Clear();
        oidc.Scope.Add(OpenIdConnectScope.OpenId);
        oidc.Scope.Add(OpenIdConnectScope.Email);
        oidc.Scope.Add("profile");

        // The ID token says who it is; nothing else is asked of the provider, and nothing of it is kept.
        oidc.SaveTokens = false;
        oidc.GetClaimsFromUserInfoEndpoint = false;
        oidc.MapInboundClaims = false;
        oidc.TokenValidationParameters.ValidIssuers = [provider.Issuer, provider.Issuer + "/"];
        oidc.TokenValidationParameters.ValidAlgorithms = Algorithms;
        oidc.TokenValidationParameters.ClockSkew = TimeSpan.FromMinutes(1);

        oidc.CallbackPath = CallbackPath(provider.Name);
        // The provider's own sign-out never reaches this app: its session is the app's, not the provider's.
        oidc.SignedOutCallbackPath = PathString.Empty;
        oidc.RemoteSignOutPath = PathString.Empty;

        // How long a visitor has at the provider, which is also how long the address they come back by
        // stays good in their browser.
        oidc.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(10);
        // The cookies that tie the provider's answer to the browser that asked: Lax, which the navigation
        // back follows, and for HTTPS only when the app is reached over HTTPS, so it works on localhost.
        foreach (var cookie in new[] { oidc.CorrelationCookie, oidc.NonceCookie })
        {
            cookie.SameSite = SameSiteMode.Lax;
            cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        }

        oidc.Events = new OpenIdConnectEvents
        {
            OnTicketReceived = OnProvenAsync,
            OnAccessDenied = OnDenied,
            OnRemoteFailure = OnFailure,
        };
    }

    /// <summary>The provider has proven someone: a session for their account, or their identity linked
    /// to the account that asked. The browser then goes back to the app.</summary>
    private static async Task OnProvenAsync(TicketReceivedContext context)
    {
        var http = context.HttpContext;
        var back = Navigation.ReturnPath(context.ReturnUri);
        string? outcome;
        try
        {
            outcome = await CompleteAsync(context);
        }
        catch (AccountException refusal)
        {
            Log(http).LogInformation("a sign-in through {Scheme} was refused: {Reason}", context.Scheme.Name, refusal.Error);
            outcome = refusal.Error switch
            {
                AccountError.RegistrationClosed => "registration_closed",
                AccountError.LinkProviderFirst => "link_provider_first",
                AccountError.ProviderLinkedElsewhere => "linked_elsewhere",
                _ => "failed",
            };
        }
        catch (Exception ex) when (!http.RequestAborted.IsCancellationRequested)
        {
            // The browser is in the middle of a navigation: it goes back to the app whatever happened
            // here (the database away, for one), and the log says what.
            Log(http).LogError(ex, "a sign-in through {Scheme} could not be recorded", context.Scheme.Name);
            outcome = "failed";
        }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Redirect(outcome is null ? back : Navigation.WithOutcome(back, OutcomeParameter, outcome));
        context.HandleResponse();
    }

    /// <summary>A new session, or <c>linked</c>, or a refusal of the account rules (thrown).</summary>
    private static async Task<string?> CompleteAsync(TicketReceivedContext context)
    {
        var http = context.HttpContext;
        var options = http.RequestServices.GetRequiredService<AccountOptions>();
        var accounts = http.RequestServices.GetRequiredService<IAccountService>();
        var provider = Find(options, context.Scheme.Name);
        var subject = context.Principal?.FindFirstValue("sub");
        if (provider is null || string.IsNullOrEmpty(subject))
        {
            Log(http).LogInformation("a sign-in through {Scheme} proved nobody", context.Scheme.Name);
            return "failed";
        }

        var identity = Identity(provider, subject, context.Principal!);
        string? asked = null;
        context.Properties?.Items.TryGetValue(LinkItem, out asked);
        if (asked is not null)
        {
            // The account that started the link is the one that comes back: the state names it, and
            // the session must still be that account's.
            var signedIn = await accounts.FindSignedInAsync(http.Request.Cookies[SessionCookie.Name], http.RequestAborted);
            if (!Guid.TryParse(asked, out var owner) || signedIn?.Id != owner)
            {
                Log(http).LogInformation("a link through {Scheme} came back to another session than the one that asked", context.Scheme.Name);
                return "failed";
            }

            await accounts.LinkProviderAsync(owner, identity, http.RequestAborted);
            return "linked";
        }

        var session = await accounts.SignInWithProviderAsync(provider, identity, http.RequestAborted);
        SessionCookie.Set(
            http.Response, session.SessionToken, options, http.RequestServices.GetRequiredService<SessionCookieOptions>());
        return null;
    }

    /// <summary>Who the ID token says it is. The issuer is the configured one, whichever of its two
    /// spellings the token used.</summary>
    private static ExternalIdentity Identity(OidcProvider provider, string subject, ClaimsPrincipal principal)
    {
        static string? Claim(ClaimsPrincipal principal, string type) =>
            principal.FindFirstValue(type) is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

        // A JSON boolean, which some providers send as a string.
        var verified = string.Equals(Claim(principal, "email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        return new ExternalIdentity(
            Issuer: provider.Issuer,
            Subject: subject,
            Email: Claim(principal, "email")?.ToLowerInvariant(),
            EmailVerified: verified,
            DisplayName: Claim(principal, "name") ?? Claim(principal, "preferred_username"));
    }

    /// <summary>The provider turned the visitor away (its own policy, or the visitor said no).</summary>
    private static Task OnDenied(AccessDeniedContext context)
    {
        Log(context.HttpContext).LogInformation("a sign-in through {Scheme} was denied by the provider", context.Scheme.Name);
        return GoBack(context.HttpContext, context.Properties, "denied", context.HandleResponse);
    }

    /// <summary>Nothing was proven: the answer did not hold (the state, the nonce, the token), the visitor
    /// came back in another browser, or the provider could not be asked. The front is told which of the
    /// two it can act on: nothing was proven, or the provider did not answer and trying again may work.</summary>
    private static Task OnFailure(RemoteFailureContext context)
    {
        var unavailable = NotAnswering(context.Failure);
        if (unavailable)
        {
            Log(context.HttpContext).LogWarning(
                "the provider of {Scheme} could not be asked: {Reason}", context.Scheme.Name, Cause(context.Failure));
        }
        else
        {
            Log(context.HttpContext).LogInformation("a sign-in through {Scheme} did not complete: {Reason}", context.Scheme.Name, context.Failure?.Message);
        }

        // Where to go back to is in the state, which a failure that came before reading it leaves unread.
        var properties = context.Properties;
        if (properties is null
            && context.Options is OpenIdConnectOptions { StateDataFormat: { } format }
            && context.Request.Query["state"].ToString() is { Length: > 0 } state)
        {
            properties = format.Unprotect(state);
        }

        return GoBack(context.HttpContext, properties, unavailable ? "unavailable" : "failed", context.HandleResponse);
    }

    private static Task GoBack(HttpContext http, AuthenticationProperties? properties, string outcome, Action handled)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Redirect(Navigation.WithOutcome(Navigation.ReturnPath(properties?.RedirectUri), OutcomeParameter, outcome));
        handled();
        return Task.CompletedTask;
    }

    /// <summary>What a provider that could not be asked comes down to, in one line: the first cause of the
    /// network's, and the one under it (a timeout, a refused connection). A provider away is a trouble to
    /// say, not a fault to trace.</summary>
    public static string Cause(Exception? failure)
    {
        var cause = failure;
        while (cause is not null and not (HttpRequestException or IOException or TimeoutException or OperationCanceledException))
        {
            cause = cause.InnerException;
        }

        cause ??= failure;
        return cause is null ? "unknown"
            : cause.InnerException is { } under ? $"{cause.Message} ({under.Message})"
            : cause.Message;
    }

    /// <summary>Whether a failure is the network's or the provider's own, anywhere in what caused it.</summary>
    public static bool NotAnswering(Exception? failure)
    {
        for (var cause = failure; cause is not null; cause = cause.InnerException)
        {
            if (cause is HttpRequestException or IOException or TimeoutException or OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    private static ILogger Log(HttpContext http) =>
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ProviderSignIn).FullName!);
}

/// <summary>The options of each provider's scheme, from the settings.</summary>
internal sealed class ProviderSchemeOptions(AccountOptions accounts, IHttpClientFactory http) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    public void Configure(OpenIdConnectOptions options)
    {
    }

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (ProviderSignIn.Find(accounts, name) is { } provider)
        {
            ProviderSignIn.Configure(options, provider, http.CreateClient(ProviderSignIn.HttpClientName));
        }
    }
}

public static class ProviderSignInPipeline
{
    /// <summary>Adds a scheme for each configured provider when the routes of a sign-in through a provider
    /// are first asked for, so that the handler answers the provider's return. Placed by the host ahead of
    /// the authentication middleware.</summary>
    public static IApplicationBuilder UseProviderSchemes(this IApplicationBuilder app) =>
        app.Use(async (context, following) =>
        {
            if (context.Request.Path.StartsWithSegments(AccountsApi.ProviderSignInPath, StringComparison.OrdinalIgnoreCase))
            {
                var schemes = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
                foreach (var provider in context.RequestServices.GetRequiredService<AccountOptions>().OidcProviders)
                {
                    var name = ProviderSignIn.Scheme(provider.Name);
                    if (await schemes.GetSchemeAsync(name) is null)
                    {
                        schemes.TryAddScheme(new AuthenticationScheme(name, provider.Label, typeof(OpenIdConnectHandler)));
                    }
                }
            }

            await following(context);
        });
}
