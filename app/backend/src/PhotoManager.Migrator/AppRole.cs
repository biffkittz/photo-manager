namespace PhotoManager.Migrator;

/// <summary>A managed identity that must exist as a PostgreSQL role with least-privilege grants.</summary>
public sealed class AppRole
{
    /// <summary>Gets or sets the role name (the managed identity's display name, e.g. <c>id-photo-web-prod</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the identity's Microsoft Entra object (principal) id.</summary>
    public Guid ObjectId { get; set; }

    /// <summary>Gets or sets which tier the role belongs to (decides its grants).</summary>
    public AppRoleKind Kind { get; set; }
}
