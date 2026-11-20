namespace PhotoManager.Data.Entities;

/// <summary>Entities whose creation/modification times are stamped by <see cref="PhotoDbContext"/>.</summary>
public interface ITimestamped
{
    /// <summary>Gets or sets when the row was created.</summary>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the row was last modified.</summary>
    DateTimeOffset UpdatedAt { get; set; }
}
