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
public sealed class LibraryController(ILibraryService library, IYearReviewService reviews) : ControllerBase
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

    [HttpGet("library/stats")]
    [Authorize]
    [EndpointSummary("The signed-in account's library in figures")]
    [EndpointDescription(
        "The totals the stores give, over the same games as the library. Steam gives totals and no history: "
        + "what was played in a year or a week is counted from the daily readings, from tracking_since on, "
        + "and is not here.")]
    [ProducesResponseType<LibraryStatsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<LibraryStatsResponse> Stats(CancellationToken ct) =>
        LibraryStatsResponse.From(await library.GetStatsAsync(Caller.Id(User), ct));

    [HttpGet("library/history")]
    [Authorize]
    [EndpointSummary("What the signed-in account played, day by day")]
    [EndpointDescription(
        "Playtime per day and per game since the library is read (tracking_since), over the same games as "
        + "the library. A day's time is the growth of the stores' totals between two readings in a row, dated "
        + "by the later one, in UTC; what a game's total was at its first reading is not split over time and "
        + "is not here. Days and games with nothing played are left out.")]
    [ProducesResponseType<LibraryHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<LibraryHistoryResponse> History(
        [FromQuery, Description("The first day to answer with, as YYYY-MM-DD; the history from its start when left out.")]
        DateOnly? from = null,
        [FromQuery, Description("The last day to answer with, as YYYY-MM-DD; the history to its end when left out. Before from, no day is answered.")]
        DateOnly? to = null,
        CancellationToken ct = default) =>
        LibraryHistoryResponse.From(await library.GetHistoryAsync(Caller.Id(User), from, to, ct));

    [HttpGet("library/years")]
    [Authorize]
    [EndpointSummary("What the signed-in account played, year by year")]
    [EndpointDescription(
        "Playtime per calendar year (UTC), the earliest first. A year is counted from the store's own review "
        + "(source review: Steam's Year in Review, every month of it, from 2022 on) when every linked account has "
        + "one, and otherwise from the daily readings (source readings: the growth of the totals, from the "
        + "first reading on). first_day and last_day say the days a year covers, and whole whether that is all of it. "
        + "A review counts every game played, owned or not; the readings count the games of the library.")]
    [ProducesResponseType<LibraryYearsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<LibraryYearsResponse> Years(CancellationToken ct) =>
        LibraryYearsResponse.From(await reviews.GetYearsAsync(Caller.Id(User), ct));
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
