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
public sealed class LibraryController(ILibraryService library, IPlaytimeService playtime) : ControllerBase
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
        + "first reading on). A year before the first reading is counted from the reviews of the accounts that "
        + "have one. first_day and last_day say the days a year covers, and whole whether that is all of it. "
        + "A review counts every game played, owned or not; the readings count the games of the library.")]
    [ProducesResponseType<LibraryYearsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<LibraryYearsResponse> Years(CancellationToken ct) =>
        LibraryYearsResponse.From(await playtime.GetYearsAsync(Caller.Id(User), ct));

    [HttpGet("library/months/{month}")]
    [Authorize]
    [EndpointSummary("What the signed-in account played in one month, game by game")]
    [EndpointDescription(
        "The games played in a month and the time of each, the most played first, counted as the month's "
        + "year is in the years of the library: from the store's review (every game played, owned or not, "
        + "which is why each comes with its name) or from the daily readings (the games of the library, "
        + "over first_day to last_day). A month nothing counts, before the first reading in a year with "
        + "no review or still to come, is answered with no source and no game.")]
    [ProducesResponseType<LibraryMonthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<LibraryMonthResponse> Month(
        [RegularExpression(Months.Pattern), Description("The month, as YYYY-MM, in UTC.")]
        string month,
        CancellationToken ct) =>
        LibraryMonthResponse.From(await playtime.GetMonthAsync(Caller.Id(User), Months.Parse(month), ct));

    [HttpDelete("library/games/{id:guid}")]
    [Authorize]
    [EndpointSummary("Take a game its store no longer lists out of the library")]
    [EndpointDescription(
        "Takes out every entry of the game that is flagged as missing (missing_since); an entry a store "
        + "still lists stays, and the game with it. 409 still_listed when no entry is missing: only a game "
        + "its store no longer lists is taken out. What was known of the game is kept, and it stays out: "
        + "no sync puts it back. If its store lists it again, the removed games say so (back_since), and "
        + "its owner puts it back or dismisses that notice.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remove([Description("The game, by its id in the catalogue.")] Guid id, CancellationToken ct)
    {
        await library.RemoveGameAsync(Caller.Id(User), id, ct);
        return NoContent();
    }

    [HttpPut("library/games/{id:guid}")]
    [Authorize]
    [EndpointSummary("Put a game back in the library")]
    [EndpointDescription(
        "Puts back everything the account took out of the game, whether its store lists it again or not: "
        + "an entry the store does not list comes back flagged as missing. 404 not_removed when nothing "
        + "of the game was taken out.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore([Description("The game, by its id in the catalogue.")] Guid id, CancellationToken ct)
    {
        await library.RestoreGameAsync(Caller.Id(User), id, ct);
        return NoContent();
    }

    [HttpGet("library/removed")]
    [Authorize]
    [EndpointSummary("The games the signed-in account took out of its library")]
    [EndpointDescription(
        "One item per game, by name, with the entries taken out. back_since is the notice to show: a "
        + "store lists the game again since that date, and the notice was not dismissed. A game taken "
        + "out is not in the library, its figures or its history; a store's review of a year still counts "
        + "what was played of it.")]
    [ProducesResponseType<RemovedGamesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<RemovedGamesResponse> Removed(CancellationToken ct) =>
        new([.. (await library.GetRemovedAsync(Caller.Id(User), ct)).Select(RemovedGameResponse.From)]);

    [HttpDelete("library/removed/{id:guid}/notice")]
    [Authorize]
    [EndpointSummary("Dismiss the notice that a removed game is back")]
    [EndpointDescription(
        "For good: the game stays out, and nothing its store says later raises the notice again. It can "
        + "still be put back. 404 not_removed when nothing of the game was taken out, 409 not_back when no "
        + "store lists it again. Dismissing a notice twice changes nothing.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Dismiss([Description("The game, by its id in the catalogue.")] Guid id, CancellationToken ct)
    {
        await library.DismissReturnAsync(Caller.Id(User), id, ct);
        return NoContent();
    }
}

/// <summary>A month, as the API spells it.</summary>
internal static class Months
{
    /// <summary>A year of four digits and a month of two: nothing else is a date.</summary>
    public const string Pattern = "^[1-9][0-9]{3}-(0[1-9]|1[0-2])$";

    public static DateOnly Parse(string month) =>
        DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
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
