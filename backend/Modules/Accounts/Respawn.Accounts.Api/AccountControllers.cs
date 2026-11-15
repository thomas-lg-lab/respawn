using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
            [.. options.Providers.Select(p => new ProviderResponse(p.Name, p.Issuer, p.ClientId))]);
    }
}

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
[AccountRefusals]
public sealed class AuthController(IAccountService accounts, AccountOptions options, SessionCookieOptions cookie) : ControllerBase
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
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<UserResponse> Login(LoginRequest body, CancellationToken ct) =>
        SignIn(await accounts.LoginAsync(body.Email, body.Password, ct));

    [HttpPost("oidc")]
    [EnableRateLimiting(AccountsApi.SignInRateLimit)]
    [EndpointSummary("Sign in with an ID token from a configured provider")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<UserResponse> Oidc(OidcRequest body, CancellationToken ct) =>
        SignIn(await accounts.SignInWithProviderAsync(
            body.Provider, body.IdToken, body.Nonce, Request.Cookies[SessionCookie.Name], ct));

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
public sealed class MeController : ControllerBase
{
    [HttpGet("me")]
    [Authorize]
    [EndpointSummary("The signed-in account")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public UserResponse Get() => UserResponse.From(HttpContext.Features.GetRequiredFeature<Account>());
}
