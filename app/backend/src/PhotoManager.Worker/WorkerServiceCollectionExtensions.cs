using Microsoft.Extensions.Options;
using PhotoManager.Core.Workflows;
using PhotoManager.Infrastructure.Configuration;
using PhotoManager.Infrastructure.Gemini;
using PhotoManager.Infrastructure.Imaging;
using PhotoManager.Infrastructure.Storage;
using PhotoManager.Worker.Activities;
using PhotoManager.Worker.Scheduling;
using PhotoManager.Worker.Workflows;
using Temporalio.Extensions.Hosting;

namespace PhotoManager.Worker;

/// <summary>Registers storage, AI, imaging and the hosted Temporal workers.</summary>
public static class WorkerServiceCollectionExtensions
{
    /// <summary>Registers everything the worker tier needs beyond the shared database/crypto/Temporal client.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">App configuration.</param>
    /// <param name="environment">Host environment.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddPhotoWorker(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddOptions<WorkerOptions>().BindConfiguration(WorkerOptions.SectionName);
        services.AddOptions<ArchiveStorageOptions>().BindConfiguration(ArchiveStorageOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<BlobStorageOptions>().BindConfiguration(BlobStorageOptions.SectionName);
        services.AddOptions<ImagingOptions>().BindConfiguration(ImagingOptions.SectionName);
        services.AddOptions<GeminiOptions>().BindConfiguration(GeminiOptions.SectionName)
            .Validate(o => o.UseFake || !string.IsNullOrWhiteSpace(o.ApiKey), "Gemini:ApiKey is required unless Gemini:UseFake is set.")
            .Validate(o => !o.UseFake || environment.IsDevelopment(), "Gemini:UseFake is only allowed in Development.")
            .ValidateOnStart();

        services.AddSingleton<IArchiveStore, WasabiArchiveStore>();
        services.AddSingleton<AzureBlobStore>();
        services.AddSingleton<IBlobStore>(sp => sp.GetRequiredService<AzureBlobStore>());
        services.AddSingleton<PhotoImageProcessor>();
        services.AddScoped<PlaintextLoader>();

        if (configuration.GetValue<bool>($"{GeminiOptions.SectionName}:{nameof(GeminiOptions.UseFake)}"))
        {
            services.AddSingleton<IPhotoDescriber, FakePhotoDescriber>();
        }
        else
        {
            services.AddHttpClient<IPhotoDescriber, GeminiPhotoDescriber>((sp, http) =>
                http.Timeout = sp.GetRequiredService<IOptions<GeminiOptions>>().Value.RequestTimeout);
        }

        AddWorkers(services, configuration.GetSection(WorkerOptions.SectionName).Get<WorkerOptions>() ?? new WorkerOptions());
        services.AddHostedService<NightlyReindexScheduler>();
        return services;
    }

    private static void AddWorkers(IServiceCollection services, WorkerOptions options)
    {
        // One Temporal worker per task queue so each workload has its own slot pool: a bulk backfill can
        // never starve an upload or an interactive download (Temporal best practice: separate queues per
        // workload/resource profile). Activities are scoped so each gets its own DbContext.
        services.AddHostedTemporalWorker(TaskQueues.Ingest)
            .ConfigureOptions(o => Tune(o, options, options.IngestMaxConcurrentActivities))
            .AddWorkflow<PhotoIngestWorkflow>()
            .AddScopedActivities<IngestActivities>()
            .AddScopedActivities<RenditionActivities>();

        services.AddHostedTemporalWorker(TaskQueues.Interactive)
            .ConfigureOptions(o => Tune(o, options, options.InteractiveMaxConcurrentActivities))
            .AddWorkflow<PrepareDownloadWorkflow>()
            .AddWorkflow<EnsureRenditionsWorkflow>()
            .AddWorkflow<DescribePhotoWorkflow>()
            .AddScopedActivities<DownloadActivities>()
            .AddScopedActivities<RenditionActivities>()
            .AddScopedActivities<DescribeActivities>();

        services.AddHostedTemporalWorker(TaskQueues.Batch)
            .ConfigureOptions(o => Tune(o, options, options.BatchMaxConcurrentActivities))
            .AddWorkflow<ReindexArchiveWorkflow>()
            .AddWorkflow<BackfillWorkflow>()
            .AddScopedActivities<MaintenanceActivities>()
            .AddScopedActivities<RenditionActivities>()
            .AddScopedActivities<DescribeActivities>();

        // Activities only: Gemini calls from any workflow are routed here and rate limited server-side.
        services.AddHostedTemporalWorker(TaskQueues.Ai)
            .ConfigureOptions(o =>
            {
                Tune(o, options, options.AiMaxConcurrentActivities);
                o.MaxTaskQueueActivitiesPerSecond = options.AiActivitiesPerSecond;
            })
            .AddScopedActivities<DescribeActivities>();
    }

    private static void Tune(TemporalWorkerServiceOptions worker, WorkerOptions options, int maxActivities)
    {
        worker.MaxConcurrentActivities = maxActivities;
        worker.GracefulShutdownTimeout = options.GracefulShutdownTimeout;
    }
}
