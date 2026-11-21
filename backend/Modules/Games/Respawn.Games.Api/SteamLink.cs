using System.Security.Claims;
using System.Text.RegularExpressions;
using AspNet.Security.OpenId;
using AspNet.Security.OpenId.Steam;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Respawn.Games.Ports;
using Respawn.SharedKernel;

namespace Respawn.Games.Api;

/// <summary>
/// "Sign in through Steam", used to link a library and never to sign in: it proves that a Steam
/// account is the signed-in user's, with nothing typed here, so nobody attaches someone else's library.
///
/// Steam speaks OpenID 2.0. The exchange (the redirect, the state that comes back, the check of
/// Steam's answer with Steam itself) is the work of a library, AspNet.Security.OpenId.Steam; what is
/// written here is what the app does with a proven SteamID. No session is opened from it.
///
/// The state is protected with the keys of ASP.NET Core Data Protection, which the app keeps in memory:
/// a link started before a restart, or on another replica, does not complete, and is started again.
/// </summary>
internal static partial class SteamLink
{
    public const string Scheme = "steam-link";

    public const string Source = "steam";

    /// <summary>Where the link starts, and under it where Steam sends the browser back.</summary>
    public const string StartPath = GamesApi.SteamLinkPath;

    public const string CallbackPath = StartPath + "/callback";

    /// <summary>Steam's sign-in, where the browser is sent and where an answer is checked. Written
    /// here rather than discovered: nothing Steam publishes can move the check elsewhere, and
    /// starting a link asks nothing of Steam.</summary>
    public const string SignIn = "https://steamcommunity.com/openid/login";

    /// <summary>The parameter the front finds on the address it is sent back to, with the outcome.</summary>
    public const string OutcomeParameter = "steam_link";

    /// <summary>The account that asked for the link, kept in the state Steam hands back.</summary>
    public const string OwnerItem = "respawn.owner";

    /// <summary>What Steam's signature must cover for its answer to prove an account.</summary>
    private static readonly string[] Signed = ["claimed_id", "identity", "return_to"];

    public static void Configure(SteamAuthenticationOptions steam)
    {
        steam.CallbackPath = CallbackPath;
        steam.Configuration = new OpenIdAuthenticationConfiguration { AuthenticationEndpoint = SignIn };
        // No key: the library would ask Steam for the profile with it, and the link needs the id only.
        steam.ApplicationKey = null;
        // Nothing is asked of Steam beyond the id.
        steam.Attributes.Clear();
        // A navigation waits on Steam's word: not for a minute.
        steam.BackchannelTimeout = TimeSpan.FromSeconds(10);
        // How long a visitor has at Steam, which is also how long the address they come back by
        // stays good in their browser.
        steam.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(10);
        // The cookie that ties Steam's answer to the browser that asked. Steam sends the browser
        // back by a plain navigation, which a Lax cookie follows; and it is for HTTPS only when the
        // app is reached over HTTPS, as the session cookie is, so the link also works on localhost.
        steam.CorrelationCookie.SameSite = SameSiteMode.Lax;
        steam.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        steam.Events.OnTicketReceived = OnProvenAsync;
        steam.Events.OnRemoteFailure = OnFailure;
    }

    /// <summary>An address of the app to send the browser back to (<see cref="Navigation.ReturnPath"/>).</summary>
    public static string ReturnPath(string? asked) => Navigation.ReturnPath(asked);

    /// <summary>The address with the link's outcome on it, once.</summary>
    public static string WithOutcome(string path, string outcome) => Navigation.WithOutcome(path, OutcomeParameter, outcome);

    /// <summary>The SteamID of a claimed identifier, when it is one of Steam's: the library checks
    /// the answer with Steam, not the shape of the identifier it proves. Steam has written it with
    /// both schemes over the years.</summary>
    public static string? SteamId(string? claimedId) =>
        claimedId is not null && ClaimedId().Match(claimedId) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>Steam has proven a SteamID: it is linked to the account that asked, and the browser
    /// goes back to the front with the outcome. No session is opened.</summary>
    private static async Task OnProvenAsync(TicketReceivedContext context)
    {
        var back = ReturnPath(context.ReturnUri);
        string outcome;
        try
        {
            outcome = await LinkAsync(context);
        }
        catch (Exception ex) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            // The browser is in the middle of a navigation: it goes back to the app whatever
            // happened here (the database away, for one), and the log says what.
            Log(context.HttpContext).LogError(ex, "a Steam link could not be recorded");
            outcome = "failed";
        }

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

        // Steam confirmed the answer; that its signature covers what is taken from it is read here too.
        var signed = http.Request.Query["openid.signed"].ToString().Split(',');
        var steamId = SteamId(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier));
        var refused = steamId is null ? "what Steam proved is not a Steam account"
            : !Signed.All(signed.Contains) ? "Steam's signature does not cover the account"
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
    /// the state did not come back, or Steam could not be asked. The front is told which of the two
    /// it can act on: nothing was proven, or Steam did not answer and trying again may work.</summary>
    private static Task OnFailure(RemoteFailureContext context)
    {
        var unavailable = Navigation.NotAnswering(context.Failure);
        if (unavailable)
        {
            Log(context.HttpContext).LogWarning(context.Failure, "Steam could not be asked to confirm a link");
        }
        else
        {
            Log(context.HttpContext).LogInformation("a Steam link did not complete: {Reason}", context.Failure?.Message);
        }

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
        context.Response.Redirect(WithOutcome(ReturnPath(properties?.RedirectUri), unavailable ? "unavailable" : "failed"));
        context.HandleResponse();
        return Task.CompletedTask;
    }

    private static ILogger Log(HttpContext http) =>
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SteamLink).FullName!);

    [GeneratedRegex(@"^https?://steamcommunity\.com/openid/id/([0-9]{17})\z", RegexOptions.CultureInvariant)]
    private static partial Regex ClaimedId();
}
