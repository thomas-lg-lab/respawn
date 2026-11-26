using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    /// games must not ask for each again at every visit.</summary>
    private const string KeptAnHour = "private, max-age=3600";

    [HttpGet("games/{id:guid}/cover")]
    [Authorize]
    [EndpointSummary("A game's cover")]
    [EndpointDescription(
        "The cover as an image, fetched from a source of the game each time it is asked for: the instance "
        + "keeps no image. Cache-Control lets the browser keep it for a week. A game without a cover answers "
        + "404 no_cover, which the browser keeps for an hour; a source that does not answer, 503 "
        + "cover_unavailable, which is not kept. Any signed-in account may ask for the cover of any game.")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> Cover([Description("The game, by its id in the catalogue.")] Guid id, CancellationToken ct)
    {
        Cover cover;
        try
        {
            cover = await covers.GetCoverAsync(id, ct);
        }
        catch (GamesException ex) when (ex.Error == GamesError.NoCover)
        {
            Response.Headers.CacheControl = KeptAnHour;
            return GamesApi.Refusal(ex.Error);
        }
        catch (SourceException)
        {
            // Said to the browser and not to the log: a source that is down would write a line per
            // image of a screen.
            Response.Headers.CacheControl = "no-store";
            return GamesApi.Error(StatusCodes.Status503ServiceUnavailable, "cover_unavailable", "The cover cannot be fetched for now.");
        }

        Response.Headers.CacheControl = KeptAWeek;

        // The source's own array when the image is the whole of it, which it is: no second copy.
        var bytes = MemoryMarshal.TryGetArray(cover.Bytes, out var held) && held.Array is { } array && held.Offset == 0 && held.Count == array.Length
            ? array
            : cover.Bytes.ToArray();
        return File(bytes, cover.ContentType);
    }
}
