using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Respawn.SharedKernel;

namespace Respawn.Accounts.Api;

/// <summary>How the session cookie is sent, which depends on where the app runs.</summary>
public sealed class SessionCookieOptions
{
    /// <summary>Sent over HTTPS only; false for plain HTTP on localhost.</summary>
    public bool Secure { get; init; } = true;
}

public static class AccountsApi
{
    /// <summary>The rate-limiter policy of the sign-in routes; the host says how many requests it allows.</summary>
    public const string SignInRateLimit = "accounts-sign-in";

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
        services.AddHttpClient(ProviderSignIn.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .AddHttpMessageHandler(() => new ProviderServerErrors());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<OpenIdConnectOptions>, ProviderSchemeOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>());

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder(SessionAuthenticationHandler.SchemeName).RequireAuthenticatedUser().Build());
        return services;
    }

    internal static ObjectResult Error(int status, string code, string message) =>
        new(new ErrorResponse(code, message)) { StatusCode = status };

    /// <summary>The status, code and message of each account refusal.</summary>
    internal static ObjectResult Refusal(AccountError error, AccountOptions options) => error switch
    {
        AccountError.InvalidEmail => Error(422, "invalid_email", "Enter a valid e-mail address."),
        AccountError.WeakPassword => Error(422, "weak_password", $"The password needs at least {options.PasswordMinLength} characters."),
        AccountError.RegistrationClosed => Error(403, "registration_closed", "Registration is closed on this instance."),
        AccountError.AccountExists => Error(409, "account_exists", "An account already uses this e-mail address."),
        AccountError.LinkProviderFirst => Error(409, "account_exists", "Sign in to your account first, then link this provider."),
        AccountError.LocalSignInDisabled => Error(403, "local_sign_in_disabled", "Local sign-in is disabled on this instance."),
        AccountError.InvalidCredentials => Error(401, "invalid_credentials", "Wrong e-mail or password, or the account is locked for a while."),
        AccountError.UnknownProvider => Error(404, "unknown_provider", "This provider is not configured."),
        AccountError.ProviderLinkedElsewhere => Error(409, "linked_elsewhere", "This identity is linked to another account of this instance."),
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
    public const string Name = "respawn_session";

    public static void Set(HttpResponse response, string token, AccountOptions accounts, SessionCookieOptions cookie) =>
        response.Cookies.Append(Name, token, new CookieOptions
        {
            MaxAge = TimeSpan.FromDays(accounts.SessionDays),
            HttpOnly = true,
            Secure = cookie.Secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });

    public static void Clear(HttpResponse response) => response.Cookies.Delete(Name, new CookieOptions { Path = "/" });
}
