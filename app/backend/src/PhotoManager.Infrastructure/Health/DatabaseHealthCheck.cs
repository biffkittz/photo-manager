using Microsoft.Extensions.Diagnostics.HealthChecks;
using PhotoManager.Data;

namespace PhotoManager.Infrastructure.Health;

/// <summary>
/// Readiness check: the database is reachable with the app's credentials. The result is cached for a few
/// seconds and refreshed by one caller at a time, because <c>/readyz</c> is reachable without signing in:
/// a flood of requests must not turn into a flood of database connections.
/// </summary>
/// <param name="db">The database context.</param>
/// <param name="cache">Process-wide cache of the last result.</param>
/// <param name="time">Clock.</param>
public sealed class DatabaseHealthCheck(PhotoDbContext db, DatabaseHealthCache cache, TimeProvider time) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (cache.TryGet(time.GetUtcNow(), out var cached))
        {
            return cached;
        }

        await cache.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have refreshed the result while this one waited.
            if (cache.TryGet(time.GetUtcNow(), out cached))
            {
                return cached;
            }

            var result = await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database is unreachable.");
            cache.Store(result, time.GetUtcNow());
            return result;
        }
        finally
        {
            cache.Gate.Release();
        }
    }
}
