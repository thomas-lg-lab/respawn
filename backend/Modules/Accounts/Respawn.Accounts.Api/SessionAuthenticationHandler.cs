using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Respawn.Accounts.Ports;
using Respawn.SharedKernel;

namespace Respawn.Accounts.Api;

/// <summary>
/// Who is asking, from the session cookie and nothing else: a request header such as Remote-User never
/// identifies anyone.
///
/// Only the routes that ask for a signed-in account look the session up: the others, /healthz and the
/// page among them, are answered without the database.
/// </summary>
internal sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Cookies[SessionCookie.Name];
        if (string.IsNullOrEmpty(token) || Context.GetEndpoint()?.Metadata.GetMetadata<IAuthorizeData>() is null)
        {
            return AuthenticateResult.NoResult();
        }

        // Resolved here, not injected: the account service comes with the database context, and a
        // request without a session is answered without it.
        var accounts = Context.RequestServices.GetRequiredService<IAccountService>();
        var account = await accounts.FindSignedInAsync(token, Context.RequestAborted);
        if (account is null)
        {
            return AuthenticateResult.NoResult();
        }

        Context.Features.Set(account);
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, account.Id.ToString())], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        WriteAsync(StatusCodes.Status401Unauthorized, "not_signed_in", "Sign in to continue.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        WriteAsync(StatusCodes.Status403Forbidden, "forbidden", "This account may not do that.");

    private Task WriteAsync(int status, string code, string message)
    {
        Response.StatusCode = status;
        return Response.WriteAsJsonAsync(new ErrorResponse(code, message), Context.RequestAborted);
    }
}
