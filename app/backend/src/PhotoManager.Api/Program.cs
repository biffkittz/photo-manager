using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PhotoManager.Api;
using PhotoManager.Api.Auth;
using PhotoManager.Api.Downloads;
using PhotoManager.Api.Jobs;
using PhotoManager.Api.Members;
using PhotoManager.Api.Photos;
using PhotoManager.Api.Security;
using PhotoManager.Api.Uploads;
using PhotoManager.Infrastructure.Azure;
using PhotoManager.Infrastructure.Configuration;
using PhotoManager.Infrastructure.Crypto;
using PhotoManager.Infrastructure.Database;
using PhotoManager.Infrastructure.Observability;
using PhotoManager.Infrastructure.Storage;
using PhotoManager.Infrastructure.Temporal;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;

    // All API endpoints except POST /api/uploads take small JSON bodies; capping the global body limit
    // prevents oversized payloads from allocating megabytes before endpoint validation runs.
    kestrel.Limits.MaxRequestBodySize = 64 * 1024;
});

var credential = AzureCredentialFactory.Create(builder.Configuration);
builder.Configuration.AddPhotoManagerKeyVault(credential);

// Split-key guard: the internet-facing tier must never hold the archive password, even by misconfiguration.
if (!string.IsNullOrEmpty(builder.Configuration["Crypto:ArchivePassword"]))
{
    throw new InvalidOperationException("The web tier must not be configured with Crypto:ArchivePassword (see docs/adr/0001-split-keys.md).");
}

builder.Services.AddSingleton(credential);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPhotoDatabase(builder.Configuration, credential);
builder.Services.AddPhotoCrypto();
builder.Services.AddPhotoTemporalClient();
builder.Services.AddPhotoTelemetry(builder.Configuration, credential);
builder.Services.AddPhotoApi(builder.Configuration, builder.Environment, credential);

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Blob:CreateContainers"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Blob:CreateContainers is only allowed in Development.");
    }

    await app.Services.GetRequiredService<AzureBlobStore>().EnsureContainersAsync(CancellationToken.None);
}

app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseStatusCodePages();

// The built SPA lives in wwwroot. Vite fingerprints everything under /assets, so those files are cached
// forever; index.html must always be revalidated so new deployments are picked up.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
        context.Context.Response.Headers.CacheControl = context.Context.Request.Path.StartsWithSegments("/assets")
            ? "public, max-age=31536000, immutable"
            : "no-cache",
});

// The OAuth callbacks (GitHub, Google) are answered inside UseAuthentication(), before the endpoint rate limiter
// below ever runs, and each callback makes the server call the provider. They get their own per-IP limit, ahead of
// authentication.
app.UseWhen(
    context => context.Request.Path.StartsWithSegments(AuthenticationSetup.CallbackPath)
        || context.Request.Path.StartsWithSegments(AuthenticationSetup.GoogleCallbackPath),
    callback => callback.UseRateLimiter(RateLimitPolicies.OAuthCallbackOptions()));

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

var api = app.MapGroup("/api")
    .RequireRateLimiting(RateLimitPolicies.Api)
    .AddEndpointFilter<AntiforgeryValidationFilter>();
api.MapAuthEndpoints();
api.MapPhotoEndpoints();
api.MapUploadEndpoints();
api.MapDownloadEndpoints();
api.MapJobEndpoints();
api.MapMemberEndpoints();

// Unknown API routes are real 404s, not the SPA shell.
app.Map("/api/{**rest}", () => Results.NotFound()).AllowAnonymous();

// Client-side routes (/gallery, /uploads, ...) serve the SPA shell; it contains no data and is public so the
// login page can render. Every API call it makes is authorized.
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
}).AllowAnonymous();

await app.RunAsync();

/// <summary>Entry point marker for WebApplicationFactory in tests.</summary>
public partial class Program;
