using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PhotoManager.Data;

/// <summary>Single place where the context's provider options are configured (runtime and design time).</summary>
public static class PhotoDbContextOptions
{
    /// <summary>Configures Npgsql with snake_case naming and a migrations history table in the default schema.</summary>
    /// <param name="builder">The options builder.</param>
    /// <param name="dataSource">The Npgsql data source (owns connection pooling and, in Azure, Entra token refresh).</param>
    /// <returns>The same builder.</returns>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(dataSource);

        return builder
            .UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
    }
}
