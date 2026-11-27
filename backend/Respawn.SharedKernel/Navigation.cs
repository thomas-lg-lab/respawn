namespace Respawn.SharedKernel;

/// <summary>
/// The browser's way back to the app after a navigation through another site (a store's or a sign-in
/// provider's): the address of the app it asked to come back to, and the outcome written on it.
/// </summary>
public static class Navigation
{
    /// <summary>The longest address to come back to: it travels in the address the other site sends the
    /// browser back to, which servers and proxies cap at a few thousand characters.</summary>
    public const int MaxReturnPath = 512;

    /// <summary>The parameter a navigation names the address to come back to with.</summary>
    public const string ReturnParameter = "return_to";

    /// <summary>The outcome of a navigation that was not let through, whatever the reason.</summary>
    public const string Failed = "failed";

    /// <summary>Whether a request comes from this site's own pages, by what the browser says in
    /// Sec-Fetch-Site, a header a page cannot set: "none" is a request the user made directly, and
    /// "same-site" would let a sibling subdomain in. A request that went through another site on its
    /// way (a proxy's own sign-in, say) is not one, though the visitor started it here.</summary>
    public static bool OwnPage(string fetchSite) => fetchSite is "same-origin" or "none";

    /// <summary>Whether a failure is the network's or the other site's own, anywhere in what caused it:
    /// the site could not be asked, and trying again may work.</summary>
    public static bool NotAnswering(Exception? failure)
    {
        for (var cause = failure; cause is not null; cause = cause.InnerException)
        {
            if (cause is HttpRequestException or IOException or TimeoutException or OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An address of the app to send the browser back to: a path of this origin, in the
    /// characters an address is written in, and never the API's. Anything else gives the home, so
    /// the parameter cannot send elsewhere, nor break the answer that carries it.</summary>
    public static string ReturnPath(string? asked) =>
        asked is { Length: > 0 and <= MaxReturnPath }
        && asked[0] == '/'
        && (asked.Length == 1 || asked[1] is not ('/' or '\\'))
        && asked.All(c => c is > ' ' and <= '~' and not '\\')
        && Plain(asked.AsSpan(0, asked.AsSpan().IndexOfAny('?', '#') is >= 0 and var end ? end : asked.Length))
        && !asked.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            ? asked
            : "/";

    /// <summary>Whether a path is read as it is written: a browser resolves "." and ".." and decodes
    /// "%61", so "/./api/..." or "/%61pi/..." would lead to the API past the check that refuses it.
    /// No screen of the app has such an address.</summary>
    private static bool Plain(ReadOnlySpan<char> path)
    {
        if (path.Contains('%'))
        {
            return false;
        }

        foreach (var segment in path.Split('/'))
        {
            if (path[segment] is "." or "..")
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The address with the outcome on it, once: an outcome the address already carried under
    /// that parameter is dropped, so nobody writes one in advance.</summary>
    public static string WithOutcome(string path, string parameter, string outcome)
    {
        var fragmentAt = path.IndexOf('#', StringComparison.Ordinal);
        var fragment = fragmentAt < 0 ? "" : path[fragmentAt..];
        var address = fragmentAt < 0 ? path : path[..fragmentAt];
        var queryAt = address.IndexOf('?', StringComparison.Ordinal);
        if (queryAt >= 0)
        {
            var kept = address[(queryAt + 1)..]
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(pair => !string.Equals(
                    Uri.UnescapeDataString(pair.Split('=')[0].Replace('+', ' ')), parameter, StringComparison.OrdinalIgnoreCase));
            var query = string.Join('&', kept);
            address = query.Length == 0 ? address[..queryAt] : address[..queryAt] + "?" + query;
        }

        var separator = address.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{address}{separator}{Uri.EscapeDataString(parameter)}={Uri.EscapeDataString(outcome)}{fragment}";
    }
}
