using System.Net;
using System.Text;
using AspNet.Security.OpenId.Steam;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Respawn.Games.Api;
using Respawn.Testing;

namespace Respawn.Games.Tests.Support;

/// <summary>
/// "Sign in through Steam" answering from memory: where Steam says its sign-in is, and what it says of
/// an answer it is asked to confirm.
///
/// Only the network is replaced. The app builds its real redirect, keeps its real state and asks its
/// real question; a test plays the browser coming back from Steam, with whatever it wants to claim.
/// </summary>
internal sealed class FakeSteamSignIn
{
    public const string SignIn = "https://steamcommunity.com/openid/login";

    /// <summary>What Steam says when asked whether an answer is its own.</summary>
    public bool Confirms { get; set; } = true;

    /// <summary>When set, Steam cannot be reached.</summary>
    public bool Unreachable { get; set; }

    /// <summary>The answers Steam was asked to confirm, as the app sent them.</summary>
    public List<Dictionary<string, string>> Asked { get; } = [];

    public Task<TestApp> StartAppAsync(Dictionary<string, string?>? settings = null) =>
        TestApp.StartAsync(settings, services =>
            services.Configure<SteamAuthenticationOptions>(SteamLink.Scheme, steam => steam.BackchannelHttpHandler = new Transport(this)));

    /// <summary>The address the browser comes back to from Steam, claiming an identifier: the address
    /// the app asked Steam to send it back to, with Steam's answer on it.</summary>
    public static string ComingBack(Uri toSteam, string claimedId, string mode = "id_res")
    {
        var returnTo = QueryHelpers.ParseQuery(toSteam.Query)["openid.return_to"].ToString();
        return QueryHelpers.AddQueryString(returnTo, new Dictionary<string, string?>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = mode,
            ["openid.op_endpoint"] = SignIn,
            ["openid.claimed_id"] = claimedId,
            ["openid.identity"] = claimedId,
            ["openid.return_to"] = returnTo,
            ["openid.response_nonce"] = "2026-11-18T10:00:00Zabcdefgh",
            ["openid.assoc_handle"] = "1234567890",
            ["openid.signed"] = "signed,op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle",
            ["openid.sig"] = "c2lnbmF0dXJl",
        });
    }

    /// <summary>The identifier Steam claims for an account.</summary>
    public static string ClaimedId(string steamId) => "https://steamcommunity.com/openid/id/" + steamId;

    private sealed class Transport(FakeSteamSignIn steam) : HttpMessageHandler
    {
        // What Steam publishes at its OpenID address: where its sign-in is.
        private const string Discovery =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <xrds:XRDS xmlns:xrds="xri://$xrds" xmlns="xri://$xrd*($v*2.0)">
              <XRD>
                <Service priority="0">
                  <Type>http://specs.openid.net/auth/2.0/server</Type>
                  <URI>https://steamcommunity.com/openid/login</URI>
                </Service>
              </XRD>
            </xrds:XRDS>
            """;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (steam.Unreachable)
            {
                throw new HttpRequestException("No such host is known.");
            }

            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsoluteUri == SignIn)
            {
                var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
                steam.Asked.Add(form.ToDictionary(field => field.Key, field => field.Value.ToString()));
                var confirmed = steam.Confirms ? "true" : "false";
                return Answer($"ns:http://specs.openid.net/auth/2.0\nis_valid:{confirmed}\n", "text/plain");
            }

            return request.RequestUri!.Host == "steamcommunity.com"
                ? Answer(Discovery, "application/xrds+xml")
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Answer(string body, string type) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, type) };
    }
}
