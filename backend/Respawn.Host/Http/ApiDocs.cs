namespace Respawn.Host.Http;

/// <summary>
/// The page that draws the API's description in a browser, at <c>/docs</c>: Swagger UI, whose files
/// come with the front's build and are served by the app like any other. Open to anyone, as the
/// description it reads already is, and it needs no database.
///
/// It is outside <c>/api/v1</c>, so it is not part of the contract, and it is a file, not a route,
/// so the description does not list it.
/// </summary>
internal static class ApiDocs
{
    private const string Root = "/docs";

    public const string Document = Root + "/index.html";

    /// <summary>
    /// The policy of this page alone: the app's, plus <c>data:</c> images, which Swagger UI draws its
    /// icons from. Nothing else is loosened: scripts and styles stay limited to the app's own files
    /// (Swagger UI styles its elements through the DOM, which a policy does not restrict), and nothing
    /// may be fetched from another origin.
    /// </summary>
    public const string ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; frame-ancestors 'none'";

    /// <summary>Whether an address is the page itself, with or without its final slash.</summary>
    public static bool IsPage(PathString path) =>
        path.Equals(Root, StringComparison.OrdinalIgnoreCase) || path.Equals(Root + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether an address is the page or one of its files.</summary>
    public static bool Covers(PathString path) => path.StartsWithSegments(Root, StringComparison.OrdinalIgnoreCase);
}
