using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PhotoManager.Infrastructure.Health;

/// <summary>Health check registration shared by the API and the worker.</summary>
public static class HealthServiceCollectionExtensions
{
    /// <summary>Adds the cached database readiness check (tag <c>ready</c>).</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The health checks builder.</returns>
    public static IHealthChecksBuilder AddPhotoHealthChecks(this IServiceCollection services)
    {
        services.TryAddSingleton<DatabaseHealthCache>();
        return services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
    }
}
