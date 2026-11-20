namespace PhotoManager.Migrator;

/// <summary>Tier of an application role.</summary>
public enum AppRoleKind
{
    /// <summary>The internet-facing API.</summary>
    Web,

    /// <summary>The Temporal workers.</summary>
    Worker,
}
