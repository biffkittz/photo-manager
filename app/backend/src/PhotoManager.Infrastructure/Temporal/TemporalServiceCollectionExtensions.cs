using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoManager.Infrastructure.Configuration;
using PhotoManager.Infrastructure.Crypto;
using Temporalio.Client;
using Temporalio.Converters;
using Temporalio.Extensions.DiagnosticSource;
using Temporalio.Extensions.OpenTelemetry;
using Temporalio.Runtime;

namespace PhotoManager.Infrastructure.Temporal;

/// <summary>Registers the Temporal client shared by the API and the worker.</summary>
public static class TemporalServiceCollectionExtensions
{
    /// <summary>Meter that carries Temporal SDK metrics (task slots, schedule-to-start latency, poller counts, ...).</summary>
    public const string MeterName = "PhotoManager.Temporal";

    /// <summary>
    /// Registers a lazily connecting <see cref="ITemporalClient"/> configured for Temporal Cloud (API key + TLS)
    /// or a local dev server, with the encrypting payload codec, encrypted failure messages, and OpenTelemetry
    /// tracing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddPhotoTemporalClient(this IServiceCollection services)
    {
        services.AddOptions<TemporalOptions>()
            .BindConfiguration(TemporalOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => CryptoServiceCollectionExtensions.IsKey(o.PayloadKey, 32), "Temporal:PayloadKey must be base64 of 32 random bytes.")
            .Validate(o => string.IsNullOrEmpty(o.PayloadKeyPrevious) || CryptoServiceCollectionExtensions.IsKey(o.PayloadKeyPrevious, 32), "Temporal:PayloadKeyPrevious must be base64 of 32 bytes.")
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TemporalOptions>>().Value;
            var keys = new List<byte[]> { Convert.FromBase64String(options.PayloadKey) };
            if (!string.IsNullOrEmpty(options.PayloadKeyPrevious))
            {
                keys.Add(Convert.FromBase64String(options.PayloadKeyPrevious));
            }

            return new AesGcmPayloadCodec(keys);
        });

        // One runtime per process; it owns the Rust core threads and routes SDK metrics into
        // System.Diagnostics.Metrics so the OpenTelemetry pipeline exports them (Temporal best practice:
        // instrument workers with SDK metrics, not only Cloud metrics).
        services.AddSingleton(_ => new TemporalRuntime(new TemporalRuntimeOptions
        {
            Telemetry = new TelemetryOptions
            {
                Metrics = new MetricsOptions { CustomMetricMeter = new CustomMetricMeter(new System.Diagnostics.Metrics.Meter(MeterName)) },
            },
        }));

        services.AddTemporalClient(_ => { });
        services.AddOptions<TemporalClientConnectOptions>()
            .Configure<IOptions<TemporalOptions>, AesGcmPayloadCodec, ILoggerFactory, TemporalRuntime>((client, temporal, codec, loggerFactory, runtime) =>
            {
                var options = temporal.Value;
                client.Runtime = runtime;
                client.TargetHost = options.Address;
                client.Namespace = options.Namespace;
                client.LoggerFactory = loggerFactory;
                client.Interceptors = [new TracingInterceptor()];
                client.DataConverter = DataConverter.Default with
                {
                    PayloadCodec = codec,

                    // Failure messages and stack traces are plain text by default; encode them too so error
                    // details (which can mention object keys) are also encrypted at rest in Temporal Cloud.
                    FailureConverter = new DefaultFailureConverter.WithEncodedCommonAttributes(),
                };

                if (!string.IsNullOrEmpty(options.ApiKey))
                {
                    client.ApiKey = options.ApiKey;
                    client.Tls = new TlsOptions();
                    client.RpcMetadata = new Dictionary<string, string> { ["temporal-namespace"] = options.Namespace };
                }
                else if (options.UseTls)
                {
                    client.Tls = new TlsOptions();
                }
            });

        services.AddSingleton<PhotoWorkflowClient>();
        return services;
    }
}
