using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using Respawn.Games.Ports;
using Respawn.SharedKernel;

namespace Respawn.Games.Api;

// The routes of one game of the catalogue, in the public API, version 1.

[ApiController]
[Route("api/v1")]
[GamesRefusals]
public sealed class GamesController(ICoverService covers, IGameService games, IPlaytimeService playtime) : ControllerBase
{
    [HttpGet("games/{id:guid}")]
    [Authorize]
    [Produces("application/json")]
    [EndpointSummary("One game, as the signed-in account sees it")]
    [EndpointDescription(
        "The catalogue's record with what the account owns of it: the entries it is owned through, as in "
        + "the library, each with its playtime and missing_since, their total, and its rank among the games "
        + "launched. Any game of the catalogue is answered: one that is not in the library has no entry "
        + "and no rank, and one the account took out of it says so in removed. What was played of it over time is its months.")]
    [ProducesResponseType<GameResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<GameResponse> Get([Description("The game, by its id in the catalogue.")] Guid id, CancellationToken ct) =>
        GameResponse.From(await games.GetGameAsync(Caller.Id(User), id, ct));

    [HttpPut("games/{id:guid}/cover-choice")]
    [Authorize]
    [Produces("application/json")]
    [EndpointSummary("Choose the cover the signed-in account sees for a game")]
    [EndpointDescription(
        "By its source, one of the game's covers' sources (covers.sources of the game). For this account "
        + "alone: the game shows it in its page and in the library, whose items name it in cover_source, to "
        + "ask the cover with. Choosing the game's own cover, the first source, is the same as choosing none. "
        + "409 no_such_cover when the game has no cover from that source.")]
    [ProducesResponseType<GameCoversResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<GameCoversResponse> ChooseCover(
        [Description("The game, by its id in the catalogue.")] Guid id, CoverChoiceRequest request, CancellationToken ct) =>
        GameCoversResponse.From(await games.ChooseCoverAsync(Caller.Id(User), id, request.Source, ct));

    [HttpDelete("games/{id:guid}/cover-choice")]
    [Authorize]
    [Produces("application/json")]
    [EndpointSummary("Go back to a game's own cover")]
    [EndpointDescription(
        "Forgets the account's choice of a cover for the game: it shows the game's own again. Forgetting a "
        + "choice that was not made changes nothing.")]
    [ProducesResponseType<GameCoversResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<GameCoversResponse> ForgetCover([Description("The game, by its id in the catalogue.")] Guid id, CancellationToken ct) =>
        GameCoversResponse.From(await games.ChooseCoverAsync(Caller.Id(User), id, null, ct));

    [HttpGet("credits")]
    [Authorize]
    [Produces("application/json")]
    [EndpointSummary("The sources the instance credits")]
    [EndpointDescription(
        "The sources whose data the instance shows and whose terms ask for a credit, each with the page it "
        + "leads to: IGDB once the instance has access to it, or holds anything it said. Empty otherwise.")]
    [ProducesResponseType<CreditsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<CreditsResponse> Credits(CancellationToken ct) =>
        CreditsResponse.From(await games.GetCreditsAsync(ct));

    [HttpGet("games/{id:guid}/months")]
    [Authorize]
    [Produces("application/json")]
    [EndpointSummary("What the signed-in account played of one game, month by month")]
    [EndpointDescription(
        "The months the game was played in and the time of each, the earliest first, counted as the years "
        + "of the library are: from the store's review when its year has one (exact to the month), from "
        + "the daily readings otherwise. The months of a year add up to what was played of the game that "
        + "year; over the same year of the library, that is its share of it, and the library's years say "
        + "which days are counted at all. A game that is not in the library may have months: a review "
        + "counts every game played. The day by day is the library's history.")]
    [ProducesResponseType<GameMonthsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<GameMonthsResponse> Months([Description("The game, by its id in the catalogue.")] Guid id, CancellationToken ct) =>
        GameMonthsResponse.From(await playtime.GetGameMonthsAsync(Caller.Id(User), id, ct));

    /// <summary>How long a browser keeps a cover: artwork changes a few times in a game's life.</summary>
    private const string KeptAWeek = "private, max-age=604800";

    /// <summary>How long a browser keeps that a game has no cover: one may come, and a screen of
    /// games must not ask for each again at every visit. Also how long it keeps a cover found before
    /// the game's store said where it is: it may be the wrong image.</summary>
    private const string KeptAnHour = "private, max-age=3600";

    private const string NotKept = "no-store";

    [HttpGet("games/{id:guid}/cover")]
    [Authorize]
    [EnableRateLimiting(GamesApi.CoverRateLimit)]
    [EndpointSummary("A game's cover")]
    [EndpointDescription(
        "The cover as an image. An instance given a data directory keeps each cover it serves: fetched from a "
        + "source of the game the first time it is asked for, then served from there, with an ETag a browser "
        + "sends back in If-None-Match to be answered 304 while it did not change; still served when its "
        + "source does not answer or no longer has it. An instance without one fetches the cover each time "
        + "it is asked for, with no ETag. Cache-Control lets the browser keep it for a week. A game without a "
        + "cover answers 404 no_cover, which the browser keeps for an hour; a source that does not answer, "
        + "503 cover_unavailable, which is not kept. Until the game's store has said where its cover is, a "
        + "few minutes after a sync, a cover is kept for an hour and no_cover is not kept. With source, the "
        + "cover of that source alone (one of the game's covers' sources, as an account's choice names it), "
        + "kept apart; the game's own cover when that source has none. Any signed-in account may ask for the "
        + "cover of any game; a client that asks for too many at once is answered 429.")]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> Cover(
        [Description("The game, by its id in the catalogue.")] Guid id,
        [FromQuery, StringLength(Limits.Source), Description("A source whose cover to give (steam, igdb), as a choice names it.")] string? source,
        CancellationToken ct)
    {
        CoverAnswer answer;
        try
        {
            answer = await covers.GetCoverAsync(id, source, ct);
        }
        catch (SourceException)
        {
            // Said to the browser and not to the log: a source that is down would write a line per
            // image of a screen.
            Response.Headers.CacheControl = NotKept;
            return GamesApi.Error(StatusCodes.Status503ServiceUnavailable, "cover_unavailable", "The cover cannot be fetched for now.");
        }

        if (answer.Cover is not { } cover)
        {
            Response.Headers.CacheControl = answer.Settled ? KeptAnHour : NotKept;
            return GamesApi.Refusal(GamesError.NoCover);
        }

        Response.Headers.CacheControl = answer.Settled ? KeptAWeek : KeptAnHour;

        // A kept image says its name: a browser that holds it asks whether it changed, and is answered
        // 304 with no body when it did not.
        return cover.Tag is { } tag
            ? File(cover.Content, cover.ContentType, lastModified: null, new EntityTagHeaderValue($"\"{tag}\""))
            : File(cover.Content, cover.ContentType);
    }
}
