using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace Respawn.Host.Http;

/// <summary>
/// The front: the files of its build, served from the web root, and its document for every address the
/// front routes itself.
///
/// The image puts the build in <c>wwwroot/</c>; the repository holds none. Without it (the app run from
/// the sources, with no front built) these addresses answer 404 like any unknown one, and the API works.
/// </summary>
internal static partial class Front
{
    private const string Document = "/index.html";

    /// <summary>The addresses that are never the front's, whatever they are followed by.</summary>
    private static readonly string[] Reserved = ["api", "healthz"];

    /// <summary>The kinds of files the app can serve, by their extension.</summary>
    private static readonly FileExtensionContentTypeProvider FileTypes = new();

    /// <summary>
    /// Serves the build's files at the root, and the document where the front routes: the front then
    /// shows the screen, or says that there is none.
    ///
    /// The document is not a route of the app: a read of one of the front's addresses is turned into a
    /// read of the document before the files are looked up. So the routes of the API never see it, and
    /// there an unknown address stays a 404 and a wrong method a 405, in the API's error format.
    /// </summary>
    public static IApplicationBuilder UseFront(this IApplicationBuilder app)
    {
        app.Use((context, next) =>
        {
            var request = context.Request;
            if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) && Routes(request.Path))
            {
                request.Path = Document;
            }

            return next(context);
        });

        // A file the build names after its content never changes and is kept for good; any other (the
        // document, the icon) is checked again on every visit.
        return app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = file => file.Context.Response.Headers[HeaderNames.CacheControl] =
                NamedAfterItsContent().IsMatch(file.File.Name) ? "public, max-age=31536000, immutable" : "no-cache",
        });
    }

    /// <summary>
    /// Whether an address is one the front routes: not the API's, not a file's.
    ///
    /// A file's address ends like a kind of file the app serves (".js", ".svg"). A dot alone does
    /// not make one: "/games/half-life.2" is a screen of the front.
    /// </summary>
    internal static bool Routes(PathString path)
    {
        var value = path.Value ?? "";
        var first = value.TrimStart('/').Split('/', 2)[0];
        return !Reserved.Contains(first, StringComparer.OrdinalIgnoreCase) && !FileTypes.TryGetContentType(value, out _);
    }

    // What the front's build appends to the scripts, style sheets and fonts it names after their
    // content: "main-QX2ZHB7T.js", "chunk-f7g-vKq6.js", "styles-5INURTSO.css". Only those kinds: a
    // file the front ships as it is (its icon) could end the same way by chance, and must stay
    // checked. The eight characters hold a capital or a digit, so a script shipped as it is under an
    // ordinary name ("user-settings.js") is not taken for one of them; a hash that happens to hold
    // neither is only checked again on each visit.
    [GeneratedRegex(@"-(?=[a-z_-]*[A-Z0-9])[A-Za-z0-9_-]{8}\.(js|css|woff2)$", RegexOptions.CultureInvariant)]
    private static partial Regex NamedAfterItsContent();
}
