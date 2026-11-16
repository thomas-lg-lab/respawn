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
public sealed class LibraryController(ILibraryService library) : ControllerBase
{
    [HttpGet("library")]
    [Authorize]
    [EndpointSummary("The games the signed-in account owns")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<LibraryResponse> Get(CancellationToken ct)
    {
        var games = await library.GetLibraryAsync(Caller.Id(User), ct);
        return new LibraryResponse([.. games.Select(LibraryGameResponse.From)]);
    }
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
