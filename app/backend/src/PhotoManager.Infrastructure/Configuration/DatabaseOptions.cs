using System.ComponentModel.DataAnnotations;

namespace PhotoManager.Infrastructure.Configuration;

/// <summary>PostgreSQL settings. Section <c>Database</c>.</summary>
public sealed class DatabaseOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Database";

    /// <summary>
    /// Gets or sets the Npgsql connection string. In Azure it has no password: the username is the managed
    /// identity's PostgreSQL role and a Microsoft Entra token is supplied as the password.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether to authenticate with Microsoft Entra tokens (Azure).</summary>
    public bool UseEntraAuthentication { get; set; }
}
