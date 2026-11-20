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

    /// <summary>An address of the app to send the browser back to: a path of this origin, in the
    /// characters an address is written in, and never the API's. Anything else gives the home, so
    /// the parameter cannot send elsewhere, nor break the answer that carries it.</summary>
    public static string ReturnPath(string? asked) =>
        asked is { Length: > 0 and <= MaxReturnPath }
        && asked[0] == '/'
        && (asked.Length == 1 || asked[1] is not ('/' or '\\'))
        && asked.All(c => c is > ' ' and <= '~' and not '\\')
        && !asked.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            ? asked
            : "/";

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
