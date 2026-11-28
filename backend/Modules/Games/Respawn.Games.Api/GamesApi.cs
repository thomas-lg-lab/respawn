using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Respawn.SharedKernel;

namespace Respawn.Games.Api;

public static class GamesApi
{
    /// <summary>The rate-limit policy of the routes that give or take away an owner's own key. The
    /// host defines it.</summary>
    public const string OwnKeyRateLimit = "games-own-key";

    /// <summary>The rate-limit policy of the returns from Steam: a return the exchange accepts costs a
    /// question to Steam, asked from the instance's address. The host defines it.</summary>
    public const string SteamReturnRateLimit = "games-steam-return";

    /// <summary>The rate-limit policy of the covers: each one asked for costs the instance a question
    /// or more to a store, from its own address. The host defines it.</summary>
    public const string CoverRateLimit = "games-cover";

    /// <summary>Where the Steam link starts, and under it where Steam sends the browser back. Behind a
    /// reverse proxy the host gives these routes the address the instance is reached at, which the
    /// address Steam is told to come back to is built from.</summary>
    public const string SteamLinkPath = "/api/v1/linked-accounts/steam/link";

    /// <summary>What drives the Games module: its routes, the Steam link, and the schedules of the
    /// sync, of the yearly reviews, of the covers' places, of the game data and of the stores' tags. The host registers a <see cref="TimeProvider"/> and a session other routes are
    /// authorized with, and gives <see cref="SteamLinkPath"/> the instance's public address.</summary>
    public static IServiceCollection AddGamesApi(this IServiceCollection services)
    {
        // A scheme of its own, that opens no session: it only answers on the link's routes.
        services.AddAuthentication().AddSteam(SteamLink.Scheme, SteamLink.Configure);
        services.AddHostedService<SyncScheduler>();
        services.AddHostedService<YearReviewScheduler>();
        services.AddHostedService<CoverScheduler>();
        services.AddHostedService<GameDataScheduler>();
        services.AddHostedService<StoreTagScheduler>();
        return services;
    }

    internal static ObjectResult Error(int status, string code, string message) =>
        new(new ErrorResponse(code, message)) { StatusCode = status };

    /// <summary>The status, code and message of each refusal of the module.</summary>
    internal static ObjectResult Refusal(GamesError error) => error switch
    {
        GamesError.UnknownLinkedAccount => Error(404, "unknown_linked_account", "No such linked account."),
        GamesError.CrossSiteLink => Error(403, "cross_site_request", "This request comes from another site."),
        GamesError.LinkedToAnotherAccount => Error(409, "linked_to_another_account", "This store account is linked to another account of this instance."),
        GamesError.SyncInProgress => Error(409, "sync_in_progress", "This store account is being read right now."),
        GamesError.SyncTooSoon => Error(429, "too_many_requests", "This store account was read a moment ago. Wait a minute and try again."),
        GamesError.InvalidCredential => Error(400, "invalid_key", "This is not a key of this store."),
        GamesError.CredentialsUnavailable => Error(503, "keys_unavailable", "This instance cannot keep a key: it has no encryption key."),
        GamesError.UnknownGame => Error(404, "unknown_game", "No such game."),
        GamesError.NoCover => Error(404, "no_cover", "This game has no cover."),
        GamesError.NoSuchCover => Error(409, "no_such_cover", "This game has no cover from that source."),
        GamesError.NotInLibrary => Error(404, "not_in_library", "This game is not in the library."),
        GamesError.StillListed => Error(409, "still_listed", "This game is still listed by its store."),
        GamesError.NotRemoved => Error(404, "not_removed", "This game was not taken out of the library."),
        GamesError.NotBack => Error(409, "not_back", "No store lists this game again."),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };
}

/// <summary>A refusal of the module is an answer, not a failure: it becomes its error response here,
/// without going through the host's exception handler and its log. Set on the module's controllers.</summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class GamesRefusalsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is GamesException refusal)
        {
            if (refusal.Error == GamesError.SyncTooSoon)
            {
                // The longest the wait can be; a client that counts shorter is refused again.
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)SyncSchedule.ManualPause.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            context.Result = GamesApi.Refusal(refusal.Error);
            context.ExceptionHandled = true;
        }
    }
}
