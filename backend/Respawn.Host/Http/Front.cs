using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
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
    private const string DocumentFile = "index.html";

    /// <summary>What the front's build leaves in the document where the nonce of a response goes.</summary>
    internal const string NoncePlaceholder = "RESPAWN_CSP_NONCE";

    /// <summary>The addresses that are never the front's, whatever they are followed by.</summary>
    private static readonly string[] Reserved = ["api", "healthz", "docs"];

    private static readonly object NonceKey = new();

    /// <summary>The kinds of files the app can serve, by their extension.</summary>
    private static readonly FileExtensionContentTypeProvider FileTypes = new();

    /// <summary>
    /// Serves the document where the front routes (the front then shows the screen, or says that there
    /// is none), and the build's files at the root.
    ///
    /// The document is not a route of the app: a read of one of the front's addresses is answered here,
    /// ahead of the routing. So the routes of the API never see it, and there an unknown address stays
    /// a 404 and a wrong method a 405, in the API's error format.
    /// </summary>
    public static IApplicationBuilder UseFront(this IApplicationBuilder app)
    {
        app.Use((context, next) =>
        {
            var request = context.Request;
            var reading = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);
            if (reading && IsDocument(request.Path))
            {
                return WriteDocumentAsync(context, next);
            }

            if (reading && NamesNoFile(context))
            {
                // A screen whose address ends like a file, or a file that is missing: which one
                // depends on how it is asked for. A cache is told so, and the 404 is not kept:
                // it must never be served to a browser that goes to the same address.
                var asksForAPage = AsksForAPage(request);
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.Vary = HeaderNames.Accept;
                    if (!asksForAPage)
                    {
                        context.Response.Headers.CacheControl = "no-store";
                    }

                    return Task.CompletedTask;
                });
                if (asksForAPage)
                {
                    return WriteDocumentAsync(context, next);
                }
            }

            // The page of the API's description is a file like any other, under its own address.
            if (reading && ApiDocs.IsPage(request.Path))
            {
                request.Path = ApiDocs.Document;
            }

            return next(context);
        });

        // A file the build names after its content never changes and is kept for good; any other (the
        // icon) is checked again on every visit.
        return app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = new ServedFileTypes(),
            OnPrepareResponse = file => file.Context.Response.Headers[HeaderNames.CacheControl] =
                NamedAfterItsContent().IsMatch(file.File.Name) ? "public, max-age=31536000, immutable" : "no-cache",
        });
    }

    /// <summary>
    /// The content security policy of a response. Everything comes from this origin and from nowhere
    /// else, with no inline script. The document alone also allows the style elements that carry its
    /// nonce: the front's components bring their styles that way. The page of the API's description
    /// has a policy of its own.
    /// </summary>
    public static string ContentSecurityPolicy(HttpContext context)
    {
        if (context.Items.TryGetValue(NonceKey, out var nonce))
        {
            return $"default-src 'self'; style-src 'self' 'nonce-{nonce}'; frame-ancestors 'none'";
        }

        return ApiDocs.Covers(context.Request.Path) ? ApiDocs.ContentSecurityPolicy : "default-src 'self'; frame-ancestors 'none'";
    }

    /// <summary>
    /// Whether an address is one the front routes: not the API's, not the API page's, not a file's.
    ///
    /// A file's address ends like a kind of file the app serves (".js", ".svg"). A dot alone does
    /// not make one: "/games/half-life.2" is a screen of the front.
    /// </summary>
    internal static bool Routes(PathString path) =>
        !IsReserved(path) && !FileTypes.TryGetContentType(path.Value ?? "", out _);

    private static bool IsReserved(PathString path)
    {
        var first = (path.Value ?? "").TrimStart('/').Split('/', 2)[0];
        return Reserved.Contains(first, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether an address of the front that ends like a file holds none: a screen that happens to
    /// be named that way ("/games/archive.zip"), or a file that is missing.
    /// </summary>
    private static bool NamesNoFile(HttpContext context)
    {
        var path = context.Request.Path;
        return !IsReserved(path)
            && !context.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider.GetFileInfo(path.Value ?? "").Exists;
    }

    /// <summary>
    /// Whether a request asks for a page. A browser that goes to an address accepts "text/html" by
    /// name; a script, a style sheet or a font it fetches for a page does not, so a file that is
    /// missing stays a 404 for them.
    /// </summary>
    private static bool AsksForAPage(HttpRequest request) =>
        request.GetTypedHeaders().Accept.Any(type => type.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether an address gets the document: one the front routes, or the document's own
    /// file, which is never sent as it is (it would carry no nonce).</summary>
    private static bool IsDocument(PathString path) =>
        Routes(path) || path.Equals("/" + DocumentFile, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes the document with a nonce of its own, the same one as in the policy of the response.
    ///
    /// The nonce is what lets the front's own style elements through and nothing else, so it must not
    /// be guessable nor reused: sixteen random bytes for each response, and a document no cache may
    /// keep, since a kept copy would meet the nonce of another response.
    /// </summary>
    private static async Task WriteDocumentAsync(HttpContext context, RequestDelegate next)
    {
        var file = context.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider.GetFileInfo(DocumentFile);
        if (!file.Exists)
        {
            // No front in this build: the address is unknown, like any other.
            await next(context);
            return;
        }

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Items[NonceKey] = nonce;

        var body = Encoding.UTF8.GetBytes((await ReadAsync(file, context.RequestAborted)).Replace(NoncePlaceholder, nonce, StringComparison.Ordinal));
        var response = context.Response;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength = body.Length;
        response.Headers[HeaderNames.CacheControl] = "no-store";
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await response.Body.WriteAsync(body, context.RequestAborted);
        }
    }

    private static async Task<string> ReadAsync(IFileInfo file, CancellationToken ct)
    {
        await using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>
    /// What a file of the build is served as: what its extension says, and plain text for a file with
    /// no extension at all. The licence and the notice that come with a library the app ships
    /// ("LICENSE", "NOTICE") are named that way, and are there to be read. A file whose extension is
    /// unknown is still not served.
    /// </summary>
    private sealed class ServedFileTypes : IContentTypeProvider
    {
        public bool TryGetContentType(string subpath, out string contentType)
        {
            if (FileTypes.TryGetContentType(subpath, out contentType!))
            {
                return true;
            }

            contentType = "text/plain; charset=utf-8";
            return !Path.HasExtension(subpath);
        }
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
