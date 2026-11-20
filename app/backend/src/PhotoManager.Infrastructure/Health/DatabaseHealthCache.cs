using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PhotoManager.Infrastructure.Health;

/// <summary>Holds the last database health result for <see cref="DatabaseHealthCheck"/> (registered as a singleton).</summary>
public sealed class DatabaseHealthCache : IDisposable
{
    /// <summary>How long a result is reused.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

    private readonly Lock _lock = new();
    private HealthCheckResult _result;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    /// <summary>Gets the gate that lets one caller at a time refresh the result.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>Returns the cached result if it is still fresh.</summary>
    /// <param name="now">Current time.</param>
    /// <param name="result">The cached result.</param>
    /// <returns>True when a fresh result exists.</returns>
    public bool TryGet(DateTimeOffset now, out HealthCheckResult result)
    {
        lock (_lock)
        {
            result = _result;
            return now < _expiresAt;
        }
    }

    /// <summary>Stores a new result.</summary>
    /// <param name="result">The result.</param>
    /// <param name="now">Current time.</param>
    public void Store(HealthCheckResult result, DateTimeOffset now)
    {
        lock (_lock)
        {
            _result = result;
            _expiresAt = now + Lifetime;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Gate.Dispose();
}
