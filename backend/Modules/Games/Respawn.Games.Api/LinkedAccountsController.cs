using System.ComponentModel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Respawn.Games.Ports;
using Respawn.SharedKernel;

namespace Respawn.Games.Api;

/// <summary>The store accounts linked to the signed-in account: which they are, linking one, reading
/// one's library now, and unlinking one.</summary>
[ApiController]
[Route("api/v1/linked-accounts")]
[Produces("application/json")]
[GamesRefusals]
public sealed class LinkedAccountsController(ILibraryService library, ISyncService sync, IHostApplicationLifetime app) : ControllerBase
{
    [HttpGet]
    [Authorize]
    [EndpointSummary("The store accounts linked to the signed-in account")]
    [ProducesResponseType<LinkedAccountsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<LinkedAccountsResponse> List(CancellationToken ct) =>
        new([.. (await library.GetLinkedAccountsAsync(Caller.Id(User), ct)).Select(LinkedAccountResponse.From)]);

    [HttpDelete("{id:guid}")]
    [Authorize]
    [EndpointSummary("Unlink a store account")]
    [EndpointDescription(
        "Forgets everything that was read from it: the library it gave, its history and the store's answers. "
        + "It cannot be undone; linking the same store account again starts from nothing.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unlink(Guid id, CancellationToken ct)
    {
        await library.UnlinkAccountAsync(Caller.Id(User), id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/sync")]
    [Authorize]
    [EndpointSummary("Read a linked account's library from its store, now")]
    [EndpointDescription(
        "The library is read every day without being asked; this reads it at once, and answers when it is "
        + "done, which takes the time the store takes to answer. A store that cannot be read is not an error "
        + "of the request: the answer is the sync with its outcome, failed, and why. Once started, the sync "
        + "goes on to its end even when the request is given up. 409 sync_in_progress: that account is being "
        + "read right now. 429 too_many_requests, with Retry-After: it was read less than a minute ago. No "
        + "sync takes a game out of the library: a game the store no longer lists is flagged (missing_since "
        + "on its library entry).")]
    [ProducesResponseType<SyncRunResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status429TooManyRequests)]
    public async Task<SyncRunResponse> Sync(Guid id) =>
        // Not the request's token: a visitor who leaves the page does not stop a reading on its way,
        // and its run is kept. The app stopping does.
        SyncRunResponse.From(await sync.SyncNowAsync(Caller.Id(User), id, app.ApplicationStopping));

    /// <summary>Where Steam sends the browser back. The exchange's handler answers there before any
    /// route does; this route only says that the address wants a session, which is how the app's
    /// sign-in knows to look the session up on it, and answers the rare request the handler lets
    /// through. Not part of the contract: nothing but Steam's redirect comes here.</summary>
    [HttpGet("steam/link/callback")]
    [Authorize]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult BackFromSteam() => Redirect(SteamLink.WithOutcome("/", "failed"));

    [HttpGet("steam/link")]
    [Authorize]
    [EndpointSummary("Link a Steam account: sends the browser to Steam")]
    [EndpointDescription(
        "A navigation from the app's own pages, not a call: the browser is sent to Steam (\"Sign in through "
        + "Steam\"), which sends it back to the app once the visitor has proven a Steam account is theirs. The "
        + "browser then lands on return_to with the outcome in the steam_link parameter: linked, "
        + "linked_elsewhere (that Steam account is linked to another account of this instance), failed "
        + "(nothing was proven) or unavailable (Steam could not be asked to confirm; trying again may work). "
        + "It links a library and never opens a session.")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task LinkSteam(
        [FromQuery(Name = "return_to"), Description("The path of the app to come back to, at most 512 characters; the home when it is not one.")]
        string? returnTo = null)
    {
        // Another site's page must not send a signed-in visitor to Steam in the app's name. The
        // browser says where a navigation comes from; one typed or bookmarked says "none".
        if (Request.Headers["Sec-Fetch-Site"].ToString() is { Length: > 0 } from && from is not ("same-origin" or "none"))
        {
            throw new GamesException(GamesError.CrossSiteLink);
        }

        var properties = new AuthenticationProperties { RedirectUri = SteamLink.ReturnPath(returnTo) };
        properties.Items[SteamLink.OwnerItem] = Caller.Id(User).ToString();
        Response.Headers.CacheControl = "no-store";
        // Nothing is asked of Steam here: the browser is sent to its sign-in.
        await HttpContext.ChallengeAsync(SteamLink.Scheme, properties);
    }
}
