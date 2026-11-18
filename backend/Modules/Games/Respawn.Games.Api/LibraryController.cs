using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Respawn.Games.Ports;
using Respawn.SharedKernel;

namespace Respawn.Games.Api;

// The routes of the Games module in the public API, version 1.

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[GamesRefusals]
public sealed class LibraryController(ILibraryService library) : ControllerBase
{
    /// <summary>The games one answer holds when the request names no limit.</summary>
    private const int DefaultLimit = 100;

    [HttpGet("library")]
    [Authorize]
    [EndpointSummary("The games the signed-in account owns")]
    [EndpointDescription(
        "One item per game, with the store entries it is owned through. Read it in parts with offset and "
        + "limit; total says how many games there are in all.")]
    [ProducesResponseType<LibraryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<LibraryResponse> Get(
        [FromQuery, RegularExpression(Sorts.Pattern), Description("The order: name (A to Z), playtime (the most played first) or last_played (the last played first). What an order cannot place comes last.")]
        string sort = Sorts.Name,
        [FromQuery, Range(0, int.MaxValue), Description("How many games to skip, in that order.")]
        int offset = 0,
        [FromQuery, Range(1, Limits.LibraryPage), Description("The most games to answer with.")]
        int limit = DefaultLimit,
        CancellationToken ct = default)
    {
        var page = await library.GetLibraryAsync(Caller.Id(User), Sorts.Parse(sort), offset, limit, ct);
        return new LibraryResponse(page.Total, offset, limit, [.. page.Games.Select(LibraryGameResponse.From)]);
    }
}

/// <summary>The orders of a library, as the API spells them.</summary>
internal static class Sorts
{
    public const string Name = "name";

    public const string Pattern = "^(name|playtime|last_played)$";

    public static LibrarySort Parse(string sort) =>
        sort switch
        {
            "playtime" => LibrarySort.Playtime,
            "last_played" => LibrarySort.LastPlayed,
            _ => LibrarySort.Name,
        };
}

/// <summary>Who is asking. This module does not know how a session is opened: the app's sign-in names
/// the account in the standard claim, and a route marked [Authorize] is only reached with it.</summary>
internal static class Caller
{
    public static Guid Id(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("the signed-in account has no id");
}
