using System.Text.Json;
using PhotoManager.Api.Auth;
using PhotoManager.Data;
using PhotoManager.Data.Entities;

namespace PhotoManager.Api.Audit;

/// <summary>Writes security-relevant events to the append-only <c>audit_events</c> table.</summary>
/// <param name="db">Database context (request scoped).</param>
/// <param name="http">HTTP context accessor.</param>
/// <param name="time">Clock.</param>
public sealed class AuditLog(PhotoDbContext db, IHttpContextAccessor http, TimeProvider time)
{
    /// <summary>Records an event. Details must never contain secrets or image content.</summary>
    /// <param name="action">Action name, e.g. <c>photo.download</c>.</param>
    /// <param name="target">Affected resource id.</param>
    /// <param name="details">Optional small JSON-serializable details.</param>
    /// <param name="actorOverride">Actor when the request is not yet authenticated (during sign-in).</param>
    /// <param name="actorIdOverride">Numeric GitHub id of <paramref name="actorOverride"/>, when known.</param>
    /// <returns>A task that completes when the event is saved.</returns>
    public async Task WriteAsync(string action, string? target, object? details, string? actorOverride = null, long? actorIdOverride = null)
    {
        var context = http.HttpContext;
        db.AuditEvents.Add(new AuditEvent
        {
            OccurredAt = time.GetUtcNow(),
            Actor = Truncate(actorOverride ?? context?.User.Login() ?? "system", 100),
            ActorId = actorIdOverride ?? (context?.User.GitHubId() is > 0 and var id ? id : null),
            Action = action,
            Target = target is null ? null : Truncate(target, 200),
            IpAddress = context?.Connection.RemoteIpAddress?.ToString(),
            Details = details is null ? null : JsonSerializer.Serialize(details),
        });
        await db.SaveChangesAsync(context?.RequestAborted ?? CancellationToken.None);
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
