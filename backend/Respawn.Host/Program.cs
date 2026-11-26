using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Respawn.Accounts.Api;
using Respawn.Games.Api;
using Respawn.Host.Configuration;
using Respawn.Host.Errors;
using Respawn.Host.Http;
using Respawn.Host.Modules;
using Respawn.Persistence;
using Respawn.SharedKernel;

// The host: /healthz, the front, the HTTP pipeline, the database, and the modules that
// provide the public API under /api/v1. Composition only: no business rule lives here.

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(console =>
{
    console.SingleLine = true;
    console.UseUtcTimestamp = true;
    console.TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ ";
});
builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

var services = builder.Services;

// Settings are built from the configuration once the host is, so a test host can supply its own, and
// all of them before the app listens: one that cannot be read stops the start (SettingsCheck).
services.AddSingleton(provider => RespawnSettings.Http(provider.GetRequiredService<IConfiguration>()));
services.AddHostedService<SettingsCheck>();
services.TryAddSingleton(TimeProvider.System);

// The one database. Nothing connects here: a module's first query does, and every module is migrated then.
services.AddDatabase(provider => RespawnSettings.Database(provider.GetRequiredService<IConfiguration>()));

// The modules. AddAuthentication, which Accounts calls, brings ASP.NET Core Data Protection along.
// A session does not use it (it is a row of the database, not an encrypted cookie); the Steam link
// and the sign-in through a provider do, for the state the other site hands back during the minute
// they take. Its keys stay in memory, so one started before a restart is started again, and its
// start-up warnings about keys that are not persisted are silenced in appsettings.json: nothing
// lasting depends on them.
services.AddAccounts();
services.AddGames();

services
    .AddControllers(mvc =>
        // Validation errors name the fields as the API spells them.
        mvc.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider(JsonNamingPolicy.SnakeCaseLower)))
    .AddJsonOptions(json => json.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower)
    .ConfigureApiBehaviorOptions(api =>
    {
        api.InvalidModelStateResponseFactory = ApiErrors.InvalidRequest;
        api.SuppressMapClientErrors = true; // no ProblemDetails: the one error format is ours
    });
// The description is built from these options, not from the controllers': without them it would spell
// the fields in camelCase while the API answers in snake_case, and call every number "a number or a
// string" (the web defaults also read a number written as text), which the front's types would repeat.
services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    json.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
services.AddOpenApi(openApi => openApi.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "respawn";
    document.Info.Version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";
    // The address the description was asked at is not part of the contract: without it the document is
    // the same wherever the app runs, so the copy the front's types are generated from can be compared.
    document.Servers?.Clear();
    return Task.CompletedTask;
}));
services.AddExceptionHandler<ApiExceptionHandler>();

// Each module names its rate-limit policies; the answer to a request over the limit is the host's.
services.AddRateLimiter(_ => { });
// A navigation is the exception: an error would be a page of JSON in the visitor's browser, so it goes
// back to the app with its outcome.
services.AddOptions<RateLimiterOptions>().Configure(limiter =>
    limiter.OnRejected = (rejected, _) =>
    {
        var http = rejected.HttpContext;
        if (http.GetEndpoint()?.Metadata.GetMetadata<NavigationAttribute>() is { } navigation)
        {
            http.Response.Headers.CacheControl = "no-store";
            // A path of this app (Navigation.ReturnPath), which LocalRedirect checks once more.
            return new ValueTask(Results.LocalRedirect(Navigation.WithOutcome(
                Navigation.ReturnPath(http.Request.Query[Navigation.ReturnParameter]), navigation.OutcomeParameter, Navigation.Failed))
                .ExecuteAsync(http));
        }

        return new ValueTask(ApiErrors.WriteAsync(http, 429, "too_many_requests", "Too many attempts. Wait a moment and try again."));
    });
services.AddOptions<ForwardedHeadersOptions>().Configure<HttpSettings>((forwarded, http) =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    forwarded.ForwardLimit = null; // walk the header from the right, past every listed proxy
    forwarded.KnownProxies.Clear();
    forwarded.KnownIPNetworks.Clear();
    foreach (var network in http.TrustedProxies)
    {
        forwarded.KnownIPNetworks.Add(network);
    }
});

var app = builder.Build();

app.UseMiddleware<ProxyWarningMiddleware>();
app.UseForwardedHeaders();
app.UseMiddleware<PublicAddressMiddleware>((IReadOnlyList<string>)[GamesApi.SteamLinkPath, AccountsApi.ProviderSignInPath]);
app.UseMiddleware<SiteGuardMiddleware>();
app.UseExceptionHandler(_ => { });
// A status without a body (an unknown route, a method that is not allowed) still answers in the one
// error format.
app.UseStatusCodePages(context => ApiErrors.WriteAsync(
    context.HttpContext,
    context.HttpContext.Response.StatusCode,
    "http_error",
    ReasonPhrases.GetReasonPhrase(context.HttpContext.Response.StatusCode)));
app.UseFront();
app.UseRouting();
app.UseRateLimiter();
app.UseProviderSchemes();
app.UseAuthentication();
app.UseAuthorization();

// Answers without the database or any secret: it says the process is up, nothing more.
app.MapGet("/healthz", () => Results.Json(new { status = "ok" })).ExcludeFromDescription();
app.MapOpenApi("/api/v1/openapi.json");
app.MapControllers();

try
{
    app.Run();
}
catch (StartRefusedException)
{
    // A setting that cannot be read, or a data directory that cannot be written: SettingsCheck said
    // which. A plain exit, not a crash on top of it.
    return 1;
}

return 0;
