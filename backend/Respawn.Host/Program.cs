using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Respawn.Host.Configuration;
using Respawn.Host.Errors;
using Respawn.Host.Http;
using Respawn.Host.Modules;
using Respawn.Persistence;

// The host: /healthz, one static page, the HTTP pipeline, the database, and the modules that
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

// Settings are built from the configuration when first needed, never at start-up.
services.AddSingleton(provider => RespawnSettings.Http(provider.GetRequiredService<IConfiguration>()));
services.TryAddSingleton(TimeProvider.System);

// The one database. Nothing connects here: a module's first query does, and every module is migrated then.
services.AddDatabase(provider => RespawnSettings.Database(provider.GetRequiredService<IConfiguration>()));

// The modules. AddAuthentication, which Accounts calls, brings ASP.NET Core Data Protection along;
// nothing uses it (a session is a row of the database, not an encrypted cookie), so its start-up
// warnings about keys that are not persisted are silenced in appsettings.json.
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
services.AddOpenApi(openApi => openApi.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "respawn";
    document.Info.Version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";
    return Task.CompletedTask;
}));
services.AddExceptionHandler<ApiExceptionHandler>();

// Each module names its rate-limit policies; the answer to a request over the limit is the host's.
services.AddRateLimiter(_ => { });
services.AddOptions<RateLimiterOptions>().Configure(limiter =>
    limiter.OnRejected = (rejected, _) => new ValueTask(ApiErrors.WriteAsync(
        rejected.HttpContext, 429, "too_many_requests", "Too many attempts. Wait a moment and try again.")));
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
app.UseMiddleware<SiteGuardMiddleware>();
app.UseExceptionHandler(_ => { });
// A status without a body (an unknown route, a method that is not allowed) still answers in the one
// error format.
app.UseStatusCodePages(context => ApiErrors.WriteAsync(
    context.HttpContext,
    context.HttpContext.Response.StatusCode,
    "http_error",
    ReasonPhrases.GetReasonPhrase(context.HttpContext.Response.StatusCode)));
app.UseStaticFiles(new StaticFileOptions { RequestPath = "/static" });
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Answers without the database or any secret: it says the process is up, nothing more.
app.MapGet("/healthz", () => Results.Json(new { status = "ok" })).ExcludeFromDescription();
app.MapGet("/", (IWebHostEnvironment environment) =>
    Results.File(Path.Combine(environment.WebRootPath, "index.html"), "text/html; charset=utf-8")).ExcludeFromDescription();
app.MapOpenApi("/api/v1/openapi.json");
app.MapControllers();

app.Run();
