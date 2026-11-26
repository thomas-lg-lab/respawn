using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Respawn.Games.Ports;
using Respawn.SharedKernel;

namespace Respawn.Games.Api;

// The routes of one game of the catalogue, in the public API, version 1.

[ApiController]
[Route("api/v1")]
[GamesRefusals]
public sealed class GamesController(ICoverService covers) : ControllerBase
{
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
        "The cover as an image, fetched from a source of the game each time it is asked for: the instance "
        + "keeps no image. Cache-Control lets the browser keep it for a week. A game without a cover answers "
        + "404 no_cover, which the browser keeps for an hour; a source that does not answer, 503 "
        + "cover_unavailable, which is not kept. Until the game's store has said where its cover is, a few "
        + "minutes after a sync, a cover is kept for an hour and no_cover is not kept. Any signed-in account "
        + "may ask for the cover of any game; a client that asks for too many at once is answered 429.")]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> Cover([Description("The game, by its id in the catalogue.")] Guid id, CancellationToken ct)
    {
        CoverAnswer answer;
        try
        {
            answer = await covers.GetCoverAsync(id, ct);
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

        // The source's own array when the image is the whole of it, which it is: no second copy.
        var bytes = MemoryMarshal.TryGetArray(cover.Bytes, out var held) && held.Array is { } array && held.Offset == 0 && held.Count == array.Length
            ? array
            : cover.Bytes.ToArray();
        return File(bytes, cover.ContentType);
    }
}
