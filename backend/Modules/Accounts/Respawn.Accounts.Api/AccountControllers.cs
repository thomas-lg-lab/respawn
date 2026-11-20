using System.ComponentModel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Respawn.Accounts.Ports;
using Respawn.SharedKernel;

namespace Respawn.Accounts.Api;

// The routes of the Accounts module in the public API, version 1.

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[AccountRefusals]
public sealed class ConfigController(IAccountService accounts) : ControllerBase
{
    [HttpGet("config")]
    [EndpointSummary("What the sign-in page may offer")]
    public async Task<ConfigResponse> Get(CancellationToken ct)
    {
        var options = await accounts.GetSignInOptionsAsync(ct);
        return new ConfigResponse(
            options.LocalSignIn,
            options.RegistrationOpen,
            options.FirstAccount,
            options.PasswordMinLength,
            [.. options.Providers.Select(p => new ProviderResponse(p.Name, p.Label))]);
    }
}

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
[AccountRefusals]
public sealed class AuthController(
    IAccountService accounts, AccountOptions options, SessionCookieOptions cookie, ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting(AccountsApi.SignInRateLimit)]
    [EndpointSummary("Create a local account and sign in")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UserResponse>> Register(RegisterRequest body, CancellationToken ct)
    {
        var signedIn = await accounts.RegisterAsync(body.Email, body.DisplayName, body.Password, ct);
        return StatusCode(StatusCodes.Status201Created, SignIn(signedIn));
    }

    [HttpPost("login")]
    [EnableRateLimiting(AccountsApi.SignInRateLimit)]
    [EndpointSummary("Sign in with a local account")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<UserResponse> Login(LoginRequest body, CancellationToken ct) =>
        SignIn(await accounts.LoginAsync(body.Email, body.Password, ct));

    [HttpGet("oidc/{provider}")]
    [EnableRateLimiting(AccountsApi.SignInRateLimit)]
    [EndpointSummary("Sign in through a configured provider: sends the browser to the provider")]
    [EndpointDescription(
        "A navigation from the app's own pages, not a call: the browser is sent to the provider, which sends it "
        + "back to the API once the visitor has signed in there. The API opens a session for the account the "
        + "provider's identity is linked to, or creates the account where registration allows it (or the provider "
        + "creates accounts), and the browser lands on return_to. It never links: a session the browser already "
        + "has is closed and replaced. Anything else lands on return_to with provider_sign_in set to "
        + "registration_closed, link_provider_first (the provider's verified e-mail is an account's: sign in to "
        + "it, then link the provider), denied (the provider turned the visitor away), failed (nothing was "
        + "proven, or the sign-in was not started: the navigation did not come from the app's own pages, the "
        + "provider is not configured, or there were too many attempts) or unavailable (the provider could not "
        + "be asked; trying again may work). The answer is always a redirect, never an error.")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [Navigation(ProviderSignIn.OutcomeParameter)]
    public Task<IActionResult> SignInWithProvider(
        string provider,
        [FromQuery(Name = "return_to"), Description("The path of the app to come back to, at most 512 characters; the home when it is not one.")]
        string? returnTo = null) =>
        StartAsync(provider, returnTo, link: false);

    [HttpGet("oidc/{provider}/link")]
    [EnableRateLimiting(AccountsApi.SignInRateLimit)]
    [EndpointSummary("Link a configured provider to the signed-in account: sends the browser to the provider")]
    [EndpointDescription(
        "A navigation from the app's own pages, not a call, and only on purpose (the account menu): the browser "
        + "is sent to the provider, and the identity it proves is linked to the account signed in when the link "
        + "started, which must still be signed in when the browser comes back. The browser lands on return_to "
        + "with provider_sign_in set to linked, linked_elsewhere (the identity is linked to another account), "
        + "denied, failed (as for a sign-in, and when nobody is signed in) or unavailable. No session is "
        + "opened. The answer is always a redirect, never an error.")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [Navigation(ProviderSignIn.OutcomeParameter)]
    public Task<IActionResult> LinkProvider(
        string provider,
        [FromQuery(Name = "return_to"), Description("The path of the app to come back to, at most 512 characters; the home when it is not one.")]
        string? returnTo = null) =>
        StartAsync(provider, returnTo, link: true);

    /// <summary>Where the provider sends the browser back. The provider's handler answers there before any
    /// route does; this route says how many returns a client may make (a return costs a question to the
    /// provider and a line of the log, and anyone can write one), in a bucket apart from the sign-in
    /// routes', and answers the rare request the handler lets through. Not part of the contract: nothing but
    /// the provider's redirect comes here.</summary>
    [HttpGet("oidc/{provider}/callback")]
    [EnableRateLimiting(AccountsApi.ProviderReturnRateLimit)]
    [Navigation(ProviderSignIn.OutcomeParameter)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult BackFromProvider(string provider) =>
        Redirect(Navigation.WithOutcome("/", ProviderSignIn.OutcomeParameter, Navigation.Failed));

    /// <summary>Sends the browser to the provider: a sign-in, or, with <paramref name="link"/>, a link to
    /// the account signed in. Which of the two is said by the route the visitor chose, never by a session
    /// the browser happens to carry: a session planted in it must not turn a sign-in into a link.
    ///
    /// A navigation: whatever stops it, the browser goes back to the app with a word it can show, never to
    /// an error, which the visitor would see as a page of JSON.</summary>
    private async Task<IActionResult> StartAsync(string provider, string? returnTo, bool link)
    {
        var back = Navigation.ReturnPath(returnTo);
        Response.Headers.CacheControl = "no-store";

        // Another site's page must not send a visitor to a provider in the app's name. The browser says
        // where a navigation comes from; one typed or bookmarked says "none". One that went through another
        // site on its way (a reverse proxy's own sign-in) is not sent on either: back in the app, the
        // visitor asks again, from its own page this time.
        if (Request.Headers["Sec-Fetch-Site"].ToString() is { Length: > 0 } from && !Navigation.OwnPage(from))
        {
            return NotStarted(back);
        }

        // From here on, the provider as the settings name it, never the word of the address.
        if (options.OidcProviders.FirstOrDefault(each => each.Name == provider) is not { } configured)
        {
            return NotStarted(back);
        }

        var properties = new AuthenticationProperties { RedirectUri = back };
        if (link)
        {
            // The route asks for no session of the pipeline, whose refusal would be an error: it looks
            // the session up itself.
            if (await accounts.FindSignedInAsync(Request.Cookies[SessionCookie.Name], HttpContext.RequestAborted) is not { } owner)
            {
                return NotStarted(back);
            }

            properties.Items[ProviderSignIn.LinkItem] = owner.Id.ToString();
        }

        try
        {
            // The provider's discovery document is read here, the first time and once a day after.
            await HttpContext.ChallengeAsync(ProviderSignIn.Scheme(configured.Name), properties);
        }
        catch (Exception ex) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            // A navigation: the browser goes back to the app with a word it can show, not an error page,
            // whether the provider did not answer or answered something that is not a discovery document.
            var unavailable = ProviderSignIn.NotAnswering(ex);
            logger.LogWarning(
                "the provider {Provider} could not be asked to start a sign-in: {Reason}", configured.Name, ProviderSignIn.Cause(ex));
            return unavailable
                ? LocalRedirect(Navigation.WithOutcome(back, ProviderSignIn.OutcomeParameter, "unavailable"))
                : NotStarted(back);
        }

        return new EmptyResult();
    }

    /// <summary>Back to the app, a path of it (Navigation.ReturnPath) which LocalRedirect checks once more.</summary>
    private LocalRedirectResult NotStarted(string back) =>
        LocalRedirect(Navigation.WithOutcome(back, ProviderSignIn.OutcomeParameter, Navigation.Failed));

    [HttpPost("logout")]
    [EndpointSummary("Sign out of this browser")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await accounts.LogoutAsync(Request.Cookies[SessionCookie.Name], ct);
        SessionCookie.Clear(Response);
        return NoContent();
    }

    private UserResponse SignIn(SignedIn signedIn)
    {
        SessionCookie.Set(Response, signedIn.SessionToken, options, cookie);
        return UserResponse.From(signedIn.Account);
    }
}

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class MeController(IAccountService accounts) : ControllerBase
{
    [HttpGet("me")]
    [Authorize]
    [EndpointSummary("The signed-in account")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public UserResponse Get() => UserResponse.From(SignedIn);

    [HttpPut("me/settings")]
    [Authorize]
    [EndpointSummary("Keep the settings of the interface on the signed-in account")]
    [EndpointDescription(
        "The language, the theme and the character, the three together, in place of the ones the account had: "
        + "the front applies them after each sign-in, whatever the browser. The answer is the account as it now is. "
        + "422 invalid_request: a setting is missing or not one of its words.")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UserResponse>> SaveSettings(SettingsRequest body, CancellationToken ct) =>
        await accounts.SaveSettingsAsync(SignedIn.Id, body.ToSettings(), ct) is { } account
            ? UserResponse.From(account)
            // The account went away during the request: its session goes with it.
            : AccountsApi.Error(StatusCodes.Status401Unauthorized, "not_signed_in", "Sign in to continue.");

    private Account SignedIn => HttpContext.Features.GetRequiredFeature<Account>();
}
