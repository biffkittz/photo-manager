using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PhotoManager.Infrastructure.Azure;
using PhotoManager.Infrastructure.Configuration;
using PhotoManager.Infrastructure.Crypto;
using PhotoManager.Infrastructure.Database;
using PhotoManager.Infrastructure.Health;
using PhotoManager.Infrastructure.Observability;
using PhotoManager.Infrastructure.Storage;
using PhotoManager.Infrastructure.Temporal;
using PhotoManager.Worker;

// Temporal worker host. No public ingress: Azure Container Apps reaches it only for health probes.
// This is the only tier that holds the archive password, Wasabi credentials and the Gemini key.
var builder = WebApplication.CreateSlimBuilder(args);

var credential = AzureCredentialFactory.Create(builder.Configuration);
builder.Configuration.AddPhotoManagerKeyVault(credential);

builder.Services.AddSingleton(credential);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPhotoDatabase(builder.Configuration, credential);
builder.Services.AddPhotoCrypto();
builder.Services.AddPhotoTemporalClient();
builder.Services.AddPhotoWorker(builder.Configuration, builder.Environment);
builder.Services.AddPhotoTelemetry(builder.Configuration, credential);
builder.Services.AddPhotoHealthChecks();

var app = builder.Build();

// Fail fast on misconfiguration instead of failing the first activity.
var crypto = app.Services.GetRequiredService<PhotoCryptoService>();
if (!crypto.CanAccessArchive)
{
    throw new InvalidOperationException("The worker requires Crypto:ArchivePassword.");
}

if (app.Configuration.GetValue<bool>("Blob:CreateContainers"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Blob:CreateContainers is only allowed in Development; Terraform owns containers in Azure.");
    }

    await app.Services.GetRequiredService<AzureBlobStore>().EnsureContainersAsync(CancellationToken.None);
}

// Liveness: the process is up. Readiness: dependencies are reachable.
app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

await app.RunAsync();
