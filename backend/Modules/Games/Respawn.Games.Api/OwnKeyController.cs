using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Respawn.Games.Ports;
using Respawn.SharedKernel;

namespace Respawn.Games.Api;

/// <summary>The owner's own key of the store, on one of their linked accounts: given, it is what every
/// sync reads that account with, and the only one tried. Never answered back.</summary>
[ApiController]
[Route("api/v1/linked-accounts/{id:guid}/key")]
[Produces("application/json")]
[Tags("LinkedAccounts")]
[GamesRefusals]
public sealed class OwnKeyController(ICredentialService credentials) : ControllerBase
{
    [HttpPut]
    [Authorize]
    [EndpointSummary("Give a linked account the owner's own key of the store")]
    [EndpointDescription(
        "Replaces the key the account had, if any. From then on every sync of that account reads it with this "
        + "key, and only this one: a key the store refuses makes the sync fail (refused, own_key true), never a "
        + "reading with the instance's key. The store is not asked here: the next sync says whether it takes the "
        + "key. The key is stored encrypted and never answered. 400 invalid_key: not in the shape of the store's "
        + "keys (Steam: a Steam Web API key, letters and digits). 503 keys_unavailable: this instance cannot "
        + "keep a key.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Set(Guid id, OwnKeyRequest request, CancellationToken ct)
    {
        await credentials.SetAsync(Caller.Id(User), id, request.Key, ct);
        return NoContent();
    }

    [HttpDelete]
    [Authorize]
    [EndpointSummary("Take the owner's own key away from a linked account")]
    [EndpointDescription(
        "The account is read with the instance's key again. Removing a key that is not there changes nothing.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        await credentials.RemoveAsync(Caller.Id(User), id, ct);
        return NoContent();
    }
}
