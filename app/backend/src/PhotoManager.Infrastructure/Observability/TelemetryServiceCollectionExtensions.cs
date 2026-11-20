using Azure.Core;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PhotoManager.Infrastructure.Temporal;
using Temporalio.Extensions.OpenTelemetry;

namespace PhotoManager.Infrastructure.Observability;

/// <summary>OpenTelemetry wiring shared by the API and the worker.</summary>
public static class TelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Exports traces, metrics (including Temporal SDK metrics) and logs to Application Insights when
    /// <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> is set (Terraform sets it in Azure). Locally nothing is
    /// exported and logs go to the console.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">App configuration.</param>
    /// <param name="credential">
    /// Managed identity used for ingestion: Application Insights has local (key) authentication disabled, so
    /// telemetry is accepted only with an Entra token of an identity holding Monitoring Metrics Publisher.
    /// </param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddPhotoTelemetry(this IServiceCollection services, IConfiguration configuration, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.IsNullOrWhiteSpace(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            return services;
        }

        services.AddOpenTelemetry()
            .UseAzureMonitor(options => options.Credential = credential)
            .WithTracing(tracing => tracing.AddSource(
                TracingInterceptor.ClientSource.Name,
                TracingInterceptor.WorkflowsSource.Name,
                TracingInterceptor.ActivitiesSource.Name))
            .WithMetrics(metrics => metrics.AddMeter(TemporalServiceCollectionExtensions.MeterName));
        return services;
    }
}
