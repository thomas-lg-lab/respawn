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

    /// <summary>Where the front's build puts the fonts its style sheet names.</summary>
    private const string BuildMedia = "/media";

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
            if (reading && IsDocument(context))
            {
                return WriteDocumentAsync(context, next);
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
    /// Whether an address gets the document: any address of the front that is not a file of its build.
    ///
    /// What is a file is what the build ships, not what an address looks like: "/games/archive.zip"
    /// and "/users/someone@example.org" are screens, whatever asks for them. Two exceptions. The
    /// document's own file is never sent as it is (it would carry no nonce), so it is written like
    /// any screen. And an address where only the build puts files (its media folder, a name that
    /// ends with a hash of the content) stays a missing file when nothing is there: a script or a
    /// font from an older build must get a 404, not a page.
    /// </summary>
    private static bool IsDocument(HttpContext context)
    {
        var path = context.Request.Path;
        if (IsReserved(path))
        {
            return false;
        }

        if (path.Equals("/" + DocumentFile, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var files = context.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;
        return !files.GetFileInfo(path.Value ?? "").Exists && !IsTheBuildsOwn(path);
    }

    private static bool IsReserved(PathString path)
    {
        var first = (path.Value ?? "").TrimStart('/').Split('/', 2)[0];
        return Reserved.Contains(first, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether only the build puts files at an address: under its media folder, or under a
    /// name that ends with a hash of the content.</summary>
    private static bool IsTheBuildsOwn(PathString path) =>
        path.StartsWithSegments(BuildMedia, StringComparison.OrdinalIgnoreCase) || NamedAfterItsContent().IsMatch(path.Value ?? "");

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
