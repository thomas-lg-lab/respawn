using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Respawn.SharedKernel;

namespace Respawn.Games.Api;

public static class GamesApi
{
    /// <summary>The routes of the Games module and the Steam link. The host registers
    /// <see cref="SteamLinkOptions"/>, and a session other routes are authorized with.</summary>
    public static IServiceCollection AddGamesApi(this IServiceCollection services)
    {
        // A scheme of its own, that opens no session: it only answers on the link's routes.
        services.AddAuthentication().AddSteam(SteamLink.Scheme, SteamLink.Configure);
        services.AddTransient<IStartupFilter, SteamLinkAddress>();
        return services;
    }

    internal static ObjectResult Error(int status, string code, string message) =>
        new(new ErrorResponse(code, message)) { StatusCode = status };

    /// <summary>The status, code and message of each refusal of the module.</summary>
    internal static ObjectResult Refusal(GamesError error) => error switch
    {
        GamesError.UnknownLinkedAccount => Error(404, "unknown_linked_account", "No such linked account."),
        GamesError.LinkedToAnotherAccount => Error(409, "linked_to_another_account", "This store account is linked to another account of this instance."),
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
            context.Result = GamesApi.Refusal(refusal.Error);
            context.ExceptionHandled = true;
        }
    }
}
