using System.ComponentModel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Respawn.Games.Ports;
using Respawn.SharedKernel;

namespace Respawn.Games.Api;

/// <summary>The store accounts linked to the signed-in account: which they are, linking one, and
/// unlinking one.</summary>
[ApiController]
[Route("api/v1/linked-accounts")]
[Produces("application/json")]
[GamesRefusals]
public sealed class LinkedAccountsController(ILibraryService library, ILogger<LinkedAccountsController> logger) : ControllerBase
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
        "A navigation, not a call: the browser is sent to Steam (\"Sign in through Steam\"), which sends it back "
        + "to the app once the visitor has proven a Steam account is theirs. The browser then lands on return_to "
        + "with the outcome in the steam_link parameter: linked, linked_elsewhere (that Steam account is linked "
        + "to another account of this instance), failed (nothing was proven) or unavailable (Steam did not "
        + "answer). It links a library and never opens a session.")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LinkSteam(
        [FromQuery(Name = "return_to"), Description("The path of the app to come back to; the home when it is not one.")]
        string? returnTo = null)
    {
        var back = SteamLink.ReturnPath(returnTo);
        var properties = new AuthenticationProperties { RedirectUri = back };
        properties.Items[SteamLink.OwnerItem] = Caller.Id(User).ToString();
        Response.Headers.CacheControl = "no-store";
        try
        {
            // The library asks Steam where its sign-in is, then sends the browser there.
            await HttpContext.ChallengeAsync(SteamLink.Scheme, properties);
            return new EmptyResult();
        }
        catch (Exception ex) when (!HttpContext.RequestAborted.IsCancellationRequested
            && ex is HttpRequestException or IOException or InvalidOperationException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Steam's sign-in could not be reached");
            return Redirect(SteamLink.WithOutcome(back, "unavailable"));
        }
    }
}
