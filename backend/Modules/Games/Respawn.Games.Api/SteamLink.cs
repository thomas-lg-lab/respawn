using System.Security.Claims;
using System.Text.RegularExpressions;
using AspNet.Security.OpenId.Steam;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Respawn.Games.Ports;

namespace Respawn.Games.Api;

/// <summary>What the link needs to know about where the app runs.</summary>
public sealed class SteamLinkOptions
{
    /// <summary>The address the instance is reached at, scheme and host, when a reverse proxy stands
    /// in front: Steam sends the browser back there. Null when the app is reached directly.</summary>
    public Uri? PublicUrl { get; init; }
}

/// <summary>
/// "Sign in through Steam", used to link a library and never to sign in: it proves that a Steam
/// account is the signed-in user's, with nothing typed here, so nobody attaches someone else's library.
///
/// Steam speaks OpenID 2.0. The exchange (the redirect, the state that comes back, the check of
/// Steam's answer with Steam itself) is the work of a library, AspNet.Security.OpenId.Steam; what is
/// written here is what the app does with a proven SteamID. No session is opened from it.
/// </summary>
internal static partial class SteamLink
{
    public const string Scheme = "steam-link";

    public const string Source = "steam";

    /// <summary>Where the link starts, and under it where Steam sends the browser back.</summary>
    public const string StartPath = "/api/v1/linked-accounts/steam/link";

    public const string CallbackPath = StartPath + "/callback";

    /// <summary>The parameter the front finds on the address it is sent back to, with the outcome.</summary>
    public const string OutcomeParameter = "steam_link";

    /// <summary>The account that asked for the link, kept in the state Steam hands back.</summary>
    public const string OwnerItem = "respawn.owner";

    public static void Configure(SteamAuthenticationOptions steam)
    {
        steam.CallbackPath = CallbackPath;
        // No key: the library would ask Steam for the profile with it, and the link needs the id only.
        steam.ApplicationKey = null;
        // Nothing is asked of Steam beyond the id.
        steam.Attributes.Clear();
        // The cookie that ties Steam's answer to the browser that asked. Steam sends the browser
        // back by a plain navigation, which a Lax cookie follows; and it is for HTTPS only when the
        // app is reached over HTTPS, as the session cookie is, so the link also works on localhost.
        steam.CorrelationCookie.SameSite = SameSiteMode.Lax;
        steam.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        steam.Events.OnTicketReceived = OnProvenAsync;
        steam.Events.OnRemoteFailure = OnFailure;
    }

    /// <summary>An address of the app to send the browser back to: a path of this origin, and never
    /// the API's. Anything else gives the home, so the parameter cannot send elsewhere.</summary>
    public static string ReturnPath(string? asked) =>
        asked is { Length: > 0 and <= 2048 }
        && asked[0] == '/'
        && (asked.Length == 1 || asked[1] is not ('/' or '\\'))
        && !asked.Contains('\\', StringComparison.Ordinal)
        && !asked.Any(char.IsControl)
        && !asked.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            ? asked
            : "/";

    public static string WithOutcome(string path, string outcome) =>
        QueryHelpers.AddQueryString(path, OutcomeParameter, outcome);

    /// <summary>The SteamID of a claimed identifier, when it is one of Steam's: the library checks
    /// the answer with Steam, not the shape of the identifier it proves.</summary>
    public static string? SteamId(string? claimedId) =>
        claimedId is not null && ClaimedId().Match(claimedId) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>Steam has proven a SteamID: it is linked to the account that asked, and the browser
    /// goes back to the front with the outcome. No session is opened.</summary>
    private static async Task OnProvenAsync(TicketReceivedContext context)
    {
        var back = ReturnPath(context.ReturnUri);
        var outcome = await LinkAsync(context);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Redirect(WithOutcome(back, outcome));
        context.HandleResponse();
    }

    private static async Task<string> LinkAsync(TicketReceivedContext context)
    {
        var http = context.HttpContext;

        // The account that started is the one that comes back: the state names it, and the session
        // must still be that account's.
        var policy = await http.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>().GetDefaultPolicyAsync();
        var session = await http.RequestServices.GetRequiredService<IPolicyEvaluator>().AuthenticateAsync(policy, http);
        var signedIn = session.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        string? asked = null;
        context.Properties?.Items.TryGetValue(OwnerItem, out asked);

        var steamId = SteamId(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier));
        var refused = steamId is null ? "what Steam proved is not a Steam account"
            : !Guid.TryParse(asked, out _) ? "the state names no account"
            : !Guid.TryParse(signedIn, out _) ? "nobody is signed in any more"
            : Guid.Parse(asked!) != Guid.Parse(signedIn!) ? "another account is signed in than the one that asked"
            : null;
        if (refused is not null)
        {
            Log(http).LogInformation("a Steam link was refused: {Reason}", refused);
            return "failed";
        }

        var owner = Guid.Parse(asked!);
        try
        {
            await http.RequestServices.GetRequiredService<ILibraryService>()
                .LinkAccountAsync(owner, Source, steamId!, displayName: null, http.RequestAborted);
            return "linked";
        }
        catch (GamesException refusal) when (refusal.Error == GamesError.LinkedToAnotherAccount)
        {
            return "linked_elsewhere";
        }
    }

    /// <summary>Steam did not prove anything: the visitor gave up there, the answer was not Steam's,
    /// or the state did not come back. The front is told, with no detail.</summary>
    private static Task OnFailure(RemoteFailureContext context)
    {
        Log(context.HttpContext).LogInformation("a Steam link did not complete: {Reason}", context.Failure?.Message);

        // Where to go back to is in the state, which a failure that came before reading it leaves
        // unread: a visitor who gave up at Steam still lands where they were.
        var properties = context.Properties;
        if (properties is null
            && context.Options is SteamAuthenticationOptions { StateDataFormat: { } format }
            && context.Request.Query["state"].ToString() is { Length: > 0 } state)
        {
            properties = format.Unprotect(state);
        }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Redirect(WithOutcome(ReturnPath(properties?.RedirectUri), "failed"));
        context.HandleResponse();
        return Task.CompletedTask;
    }

    private static ILogger Log(HttpContext http) =>
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SteamLink).FullName!);

    [GeneratedRegex(@"^https?://steamcommunity\.com/openid/id/([0-9]{17})$", RegexOptions.CultureInvariant)]
    private static partial Regex ClaimedId();
}

/// <summary>Behind a reverse proxy, the link's routes see the address the instance is reached at: the
/// address Steam is given to come back to is built from the request. Only these routes, so nothing
/// else of the app depends on the setting.</summary>
internal sealed class SteamLinkAddress : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, following) =>
        {
            if (context.Request.Path.StartsWithSegments(SteamLink.StartPath, StringComparison.OrdinalIgnoreCase)
                && context.RequestServices.GetRequiredService<SteamLinkOptions>().PublicUrl is { } address)
            {
                context.Request.Scheme = address.Scheme;
                context.Request.Host = address.IsDefaultPort ? new HostString(address.Host) : new HostString(address.Host, address.Port);
            }

            return following(context);
        });
        next(app);
    };
}
