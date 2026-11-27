using Respawn.Host.Configuration;
using Respawn.Host.Errors;
using Respawn.SharedKernel;

namespace Respawn.Host.Http;

/// <summary>
/// Reports, once, a reverse proxy that RESPAWN_TRUSTED_PROXIES does not list.
///
/// Such a proxy adds X-Forwarded-For and the app ignores it: every visitor then shares the proxy's
/// address, and one sign-in rate limit. The header is only looked at to notice that, never to choose
/// the client's address. Runs before the forwarded headers are applied, on the peer's own address.
/// </summary>
internal sealed class ProxyWarningMiddleware(RequestDelegate next, HttpSettings settings, ILogger<ProxyWarningMiddleware> logger)
{
    private int _warned;

    public Task InvokeAsync(HttpContext context)
    {
        if (Volatile.Read(ref _warned) == 0
            && context.Request.Headers.ContainsKey("X-Forwarded-For")
            && context.Connection.RemoteIpAddress is { } peer
            && !settings.IsTrustedProxy(peer)
            && Interlocked.Exchange(ref _warned, 1) == 0) // once: a request triggers it, it must not fill the log
        {
            logger.LogWarning(
                "a request came with X-Forwarded-For from an address RESPAWN_TRUSTED_PROXIES does not list: if "
                + "that is a reverse proxy, all its visitors share one sign-in rate limit until it is listed");
        }

        return next(context);
    }
}

/// <summary>Security headers on every response, an answer of the API kept by nobody unless its route
/// says otherwise (it is one account's), and the rule that goes with a cookie session: a
/// state-changing request must come from this site's own pages.</summary>
internal sealed class SiteGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "same-origin";
            headers.ContentSecurityPolicy = Front.ContentSecurityPolicy(context);
            if (headers.CacheControl.Count == 0 && context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });

        var method = context.Request.Method;
        var unsafeMethod = HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
        if (unsafeMethod && !SameSite(context.Request))
        {
            await ApiErrors.WriteAsync(context, 403, "cross_site_request", "This request comes from another site.");
            return;
        }

        await next(context);
    }

    /// <summary>
    /// Whether a state-changing request comes from this site's own pages.
    ///
    /// The browser says so itself in Sec-Fetch-Site, a header a page cannot set: that answer does not
    /// depend on the Host header, so it holds behind any reverse proxy. A browser too old to send it is
    /// judged on its Origin, compared with the host the request names. A request with neither header
    /// does not come from a browser page. X-Forwarded-Host is not read.
    /// </summary>
    internal static bool SameSite(HttpRequest request)
    {
        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (fetchSite.Length > 0)
        {
            return Navigation.OwnPage(fetchSite);
        }

        var origin = request.Headers.Origin.ToString();
        if (origin.Length == 0)
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Behind a reverse proxy, the routes that send the browser through another site see the address the
/// instance is reached at (RESPAWN_PUBLIC_URL): the address the other site is told to send the browser
/// back to is built from the request. Only these routes, so nothing else of the app depends on the
/// setting. Placed ahead of what reads a request's address.
/// </summary>
internal sealed class PublicAddressMiddleware(RequestDelegate next, HttpSettings settings, IReadOnlyList<string> paths)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (settings.PublicUrl is { } address
            && paths.Any(path => context.Request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase)))
        {
            context.Request.Scheme = address.Scheme;
            context.Request.Host = address.IsDefaultPort ? new HostString(address.Host) : new HostString(address.Host, address.Port);
        }

        return next(context);
    }
}
