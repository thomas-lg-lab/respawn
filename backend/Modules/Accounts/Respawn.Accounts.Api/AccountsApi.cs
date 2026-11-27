using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Respawn.Accounts.Ports;
using Respawn.SharedKernel;

namespace Respawn.Accounts.Api;

/// <summary>How the session cookie is sent, which depends on where the app runs.</summary>
public sealed class SessionCookieOptions
{
    /// <summary>Sent over HTTPS only; false for plain HTTP on localhost.</summary>
    public bool Secure { get; init; } = true;

    /// <summary>The cookie's name. Over HTTPS it carries the <c>__Host-</c> prefix: a browser then takes
    /// it from this host only, for every path and with no Domain, so another service on a neighbouring
    /// subdomain cannot plant a session of its own in a visitor's browser. The prefix needs Secure,
    /// which plain HTTP on localhost does not have.</summary>
    public string Name => Prefixed("respawn_session");

    /// <summary>A cookie's name as this instance sends it: with the prefix over HTTPS.</summary>
    public string Prefixed(string name) => Secure ? "__Host-" + name : name;
}

public static class AccountsApi
{
    /// <summary>The rate-limiter policy of the sign-in routes; the host says how many requests it allows.</summary>
    public const string SignInRateLimit = "accounts-sign-in";

    /// <summary>The rate-limiter policy of the returns from a provider, apart from the sign-in routes'
    /// so that neither uses up the other; the host says how many requests it allows.</summary>
    public const string ProviderReturnRateLimit = "accounts-provider-return";

    /// <summary>Where a sign-in through a provider starts (<c>/{provider}</c>) and comes back
    /// (<c>/{provider}/callback</c>). Behind a reverse proxy the host gives these routes the address the
    /// instance is reached at, which the address the provider is told to come back to is built from.</summary>
    public const string ProviderSignInPath = "/api/v1/auth/oidc";

    /// <summary>The routes of the Accounts module and the session they open. The host registers
    /// <see cref="AccountOptions"/> and <see cref="SessionCookieOptions"/>, places
    /// <see cref="ProviderSignInPipeline.UseProviderSchemes"/> ahead of its authentication, and gives
    /// <see cref="ProviderSignInPath"/> the instance's public address.</summary>
    public static IServiceCollection AddAccountsApi(this IServiceCollection services)
    {
        // Who is asking, for every module: a route marked [Authorize] wants a session of this module.
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, null);

        // Sign-in through a provider: one OpenID Connect scheme per provider, added when first needed.
        // A scheme keeps its client for as long as the app runs, so the factory never gives it a new
        // handler: its connections are closed after a few minutes instead, and the next one looks the
        // provider's address up again.
        services.AddHttpClient(ProviderSignIn.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            .AddHttpMessageHandler(() => new ProviderServerErrors());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<OpenIdConnectOptions>, ProviderSchemeOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>());

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder(SessionAuthenticationHandler.SchemeName).RequireAuthenticatedUser().Build())
            .AddPolicy(Administrator, policy => policy.AddAuthenticationSchemes(SessionAuthenticationHandler.SchemeName).RequireRole(Administrator));
        return services;
    }

    /// <summary>The role of an administrator, and the policy a route asks for it with.</summary>
    internal const string Administrator = "administrator";

    internal static ObjectResult Error(int status, string code, string message) =>
        new(new ErrorResponse(code, message)) { StatusCode = status };

    /// <summary>The status, code and message of each account refusal a route answers. The refusals of a
    /// sign-in through a provider are not here: they are outcomes on the address the browser comes back to
    /// (<see cref="ProviderSignIn"/>).</summary>
    internal static ObjectResult Refusal(AccountError error, AccountOptions options) => error switch
    {
        AccountError.InvalidEmail => Error(422, "invalid_email", "Enter a valid e-mail address."),
        AccountError.WeakPassword => Error(422, "weak_password", $"The password needs at least {options.PasswordMinLength} characters."),
        AccountError.RegistrationClosed => Error(403, "registration_closed", "Registration is closed on this instance."),
        AccountError.AccountExists => Error(409, "account_exists", "An account already uses this e-mail address."),
        AccountError.LocalSignInDisabled => Error(403, "local_sign_in_disabled", "Local sign-in is disabled on this instance."),
        AccountError.Forbidden => Error(403, "forbidden", "This account may not do that."),
        AccountError.InvalidCredentials => Error(401, "invalid_credentials", "Wrong e-mail or password, or the account is locked for a while."),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };
}

/// <summary>An account refusal is an answer, not a failure: it becomes its error response here, without
/// going through the host's exception handler and its log. Set on the module's controllers.</summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class AccountRefusalsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is AccountException refusal)
        {
            var options = context.HttpContext.RequestServices.GetRequiredService<AccountOptions>();
            context.Result = AccountsApi.Refusal(refusal.Error, options);
            context.ExceptionHandled = true;
        }
    }
}

/// <summary>The session cookie: an opaque token, HttpOnly, whose SHA-256 names a row of the database.</summary>
internal static class SessionCookie
{
    /// <summary>The cookie's name without its prefix: what it was over HTTPS too, before.</summary>
    private const string Unprefixed = "respawn_session";

    /// <summary>The token the request carries, if any.</summary>
    public static string? Carried(HttpContext http) =>
        http.Request.Cookies[http.RequestServices.GetRequiredService<SessionCookieOptions>().Name];

    public static void Set(HttpResponse response, string token, AccountOptions accounts, SessionCookieOptions cookie) =>
        response.Cookies.Append(cookie.Name, token, new CookieOptions
        {
            MaxAge = TimeSpan.FromDays(accounts.SessionDays),
            HttpOnly = true,
            Secure = cookie.Secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });

    /// <summary>Gives the browser a new session in place of the one it carried, which is closed too: it
    /// would otherwise stay good until it expires, with no sign-out left to close it. The sign-in stands
    /// whether or not the old session could be closed; the log says when it could not.</summary>
    public static async Task ReplaceAsync(
        HttpContext http, IAccountService accountService, string token, AccountOptions accounts, SessionCookieOptions cookie, ILogger logger)
    {
        var carried = Carried(http);
        Set(http.Response, token, accounts, cookie);
        try
        {
            await accountService.LogoutAsync(carried, http.RequestAborted);
        }
        catch (Exception ex) when (!http.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning("the session a sign-in replaced could not be closed: {Reason}", ex.Message);
        }

        await CloseUnprefixedAsync(http, accountService, cookie, logger);
    }

    /// <summary>Over HTTPS, closes the session a browser still carries under the name of before the
    /// prefix, and takes that cookie away: it is no longer read, and would otherwise stay good in the
    /// database until it expires. Asked at each sign-in and sign-out; nothing to do once every such
    /// cookie has expired.</summary>
    public static async Task CloseUnprefixedAsync(HttpContext http, IAccountService accountService, SessionCookieOptions cookie, ILogger logger)
    {
        if (cookie.Name == Unprefixed || http.Request.Cookies[Unprefixed] is not { Length: > 0 } carried)
        {
            return;
        }

        http.Response.Cookies.Delete(Unprefixed, new CookieOptions { Path = "/" });
        try
        {
            await accountService.LogoutAsync(carried, http.RequestAborted);
        }
        catch (Exception ex) when (!http.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning("a session of before the cookie's prefix could not be closed: {Reason}", ex.Message);
        }
    }

    /// <summary>Takes the cookie away, said as it was set: a browser ignores what a prefixed cookie is
    /// told without Secure.</summary>
    public static void Clear(HttpResponse response, SessionCookieOptions cookie) =>
        response.Cookies.Delete(cookie.Name, new CookieOptions { Path = "/", Secure = cookie.Secure });
}
