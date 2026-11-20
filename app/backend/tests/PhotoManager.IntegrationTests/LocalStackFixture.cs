using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PhotoManager.Data;
using PhotoManager.Infrastructure.Azure;
using PhotoManager.Infrastructure.Crypto;
using PhotoManager.Infrastructure.Database;
using PhotoManager.Infrastructure.Storage;
using PhotoManager.Infrastructure.Temporal;
using PhotoManager.Worker;

namespace PhotoManager.IntegrationTests;

/// <summary>
/// Hosts the real worker in-process, wired to the docker-compose services, and exposes the same services the
/// API uses. Secrets come from the repository's .env (created by scripts/dev-secrets.sh).
/// </summary>
public sealed class LocalStackFixture : IAsyncLifetime
{
    private IHost? _host;

    /// <summary>Gets a value indicating whether integration tests are enabled.</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable("PHOTOMANAGER_INTEGRATION") == "1";

    /// <summary>
    /// Gets a value indicating whether workflows should be executed by an already running worker (for example the
    /// container from `docker compose --profile app`) instead of one hosted in this test process.
    /// </summary>
    public static bool ExternalWorker => Environment.GetEnvironmentVariable("PHOTOMANAGER_INTEGRATION_EXTERNAL_WORKER") == "1";

    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Integration tests are disabled.");

    public string ArchivePassword { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        if (!Enabled)
        {
            return;
        }

        var secrets = ReadDotEnv();
        ArchivePassword = secrets["DEV_ARCHIVE_PASSWORD"];
        var settings = new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Host=localhost;Port=55432;Database=photomanager;Username=photomanager;Password=photomanager-dev",
            ["Blob:ConnectionString"] = "UseDevelopmentStorage=true",
            ["Archive:ServiceUrl"] = "http://localhost:9090",
            ["Archive:Region"] = "us-east-1",
            ["Archive:BucketName"] = "photo-bk-dev",
            ["Archive:AccessKeyId"] = "local-dev",
            ["Archive:SecretAccessKey"] = "local-dev",
            ["Archive:ForcePathStyle"] = "true",
            ["Temporal:Address"] = "localhost:7233",
            ["Temporal:Namespace"] = "default",
            ["Temporal:PayloadKey"] = secrets["TEMPORAL_PAYLOAD_KEY"],
            ["Crypto:WebKey"] = secrets["CRYPTO_WEB_KEY"],
            ["Crypto:ArchivePassword"] = ArchivePassword,
            ["Gemini:UseFake"] = "true",
            ["Worker:NightlyReindexEnabled"] = "false",
        };

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development });
        builder.Configuration.AddInMemoryCollection(settings);
        var credential = AzureCredentialFactory.Create(builder.Configuration);
        builder.Services.AddSingleton(credential);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddPhotoDatabase(builder.Configuration, credential);
        builder.Services.AddPhotoCrypto();
        builder.Services.AddPhotoTemporalClient();
        builder.Services.AddPhotoWorker(builder.Configuration, builder.Environment);
        if (ExternalWorker)
        {
            // Keep the storage/crypto services but don't poll: an external worker must do the processing.
            builder.Services.RemoveAll<IHostedService>();
        }

        _host = builder.Build();
        await _host.Services.GetRequiredService<AzureBlobStore>().EnsureContainersAsync(CancellationToken.None);
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PhotoDbContext>().Database.MigrateAsync();
        }

        await _host.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    public PhotoWorkflowClient Workflows => Services.GetRequiredService<PhotoWorkflowClient>();

    public IBlobStore Blobs => Services.GetRequiredService<IBlobStore>();

    public IArchiveStore Archive => Services.GetRequiredService<IArchiveStore>();

    public PhotoCryptoService Crypto => Services.GetRequiredService<PhotoCryptoService>();

    /// <summary>Runs <paramref name="action"/> with a fresh DbContext (so polling never sees stale tracked state).</summary>
    public async Task<T> WithDbAsync<T>(Func<PhotoDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<PhotoDbContext>());
    }

    /// <summary>Polls until <paramref name="condition"/> holds or the timeout elapses.</summary>
    public async Task<T> WaitForAsync<T>(Func<PhotoDbContext, Task<T>> read, Func<T, bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(90));
        while (true)
        {
            var value = await WithDbAsync(read);
            if (condition(value))
            {
                return value;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Condition not met in time; last value: {value}");
            }

            await Task.Delay(500);
        }
    }

    private static Dictionary<string, string> ReadDotEnv()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".env")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("No .env found; run scripts/dev-secrets.sh first.");
        }

        return File.ReadAllLines(Path.Combine(directory.FullName, ".env"))
            .Where(l => l.Contains('=') && !l.TrimStart().StartsWith('#'))
            .Select(l => l.Split('=', 2))
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
    }
}

[CollectionDefinition(Name)]
public sealed class LocalStackCollection : ICollectionFixture<LocalStackFixture>
{
    public const string Name = "local-stack";
}
