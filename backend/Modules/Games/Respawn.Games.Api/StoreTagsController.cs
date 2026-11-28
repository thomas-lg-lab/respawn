using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Respawn.Games.Ports;
using Respawn.SharedKernel;

namespace Respawn.Games.Api;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[GamesRefusals]
public sealed class StoreTagsController(IStoreTagService tags) : ControllerBase
{
    [HttpGet("store-tags")]
    [Authorize]
    [EndpointSummary("The names of the tags the stores' players gave the games")]
    [EndpointDescription(
        "Every tag a library game may carry in store_tags, with its name as its store gives it in each "
        + "language it was asked in. The same for every account: a store's tags are the instance's.")]
    [ProducesResponseType<StoreTagsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<StoreTagsResponse> Get(CancellationToken ct) => StoreTagsResponse.From(await tags.GetNamesAsync(ct));
}
