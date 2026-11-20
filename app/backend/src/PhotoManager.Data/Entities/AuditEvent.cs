namespace PhotoManager.Data.Entities;

/// <summary>
/// Append-only security audit trail (sign-ins, denials, uploads, downloads, maintenance actions).
/// The application roles are granted INSERT/SELECT only on this table; rows are never updated.
/// </summary>
public sealed class AuditEvent
{
    /// <summary>Gets or sets the identifier.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets when the event happened.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Gets or sets the GitHub login (or "anonymous"/"system").</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>Gets or sets the actor's numeric GitHub id, when known (logins can change; ids cannot).</summary>
    public long? ActorId { get; set; }

    /// <summary>Gets or sets the action name, for example <c>auth.signin</c> or <c>photo.download</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Gets or sets the affected resource, for example a photo id.</summary>
    public string? Target { get; set; }

    /// <summary>Gets or sets the client IP address (from the trusted forwarded-for header).</summary>
    public string? IpAddress { get; set; }

    /// <summary>Gets or sets additional JSON details (never secrets or image content).</summary>
    public string? Details { get; set; }
}
