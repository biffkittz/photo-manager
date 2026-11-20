using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PhotoManager.Data;
using PhotoManager.Infrastructure.Configuration;

namespace PhotoManager.Infrastructure.Database;

/// <summary>Registers the PostgreSQL data source and <see cref="PhotoDbContext"/>.</summary>
public static class DatabaseServiceCollectionExtensions
{
    /// <summary>How often the Entra token used as the PostgreSQL password is refreshed (below Azure.Identity's 5-minute margin).</summary>
    public static readonly TimeSpan EntraTokenRefreshInterval = TimeSpan.FromMinutes(4);

    private static readonly string[] EntraScopes = ["https://ossrdbms-aad.database.windows.net/.default"];

    /// <summary>
    /// Registers a pooled <see cref="NpgsqlDataSource"/> and the EF Core context. With
    /// <see cref="DatabaseOptions.UseEntraAuthentication"/>, passwords are short-lived Microsoft Entra
    /// tokens for the app's managed identity, refreshed in the background. No database password exists anywhere.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">App configuration.</param>
    /// <param name="credential">Azure credential used to obtain Entra tokens.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddPhotoDatabase(this IServiceCollection services, IConfiguration configuration, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(credential);

        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
            ?? throw new InvalidOperationException("Missing 'Database' configuration section.");
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("Database:ConnectionString is required.");
        }

        services.AddSingleton(_ => BuildDataSource(options, credential));
        services.AddDbContext<PhotoDbContext>((sp, builder) =>
            PhotoDbContextOptions.Configure(builder, sp.GetRequiredService<NpgsqlDataSource>()));
        return services;
    }

    /// <summary>Builds a data source, wiring Entra token authentication when enabled.</summary>
    /// <param name="options">Database options.</param>
    /// <param name="credential">Azure credential.</param>
    /// <returns>The data source.</returns>
    public static NpgsqlDataSource BuildDataSource(DatabaseOptions options, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credential);

        var builder = new NpgsqlDataSourceBuilder(options.ConnectionString);
        if (options.UseEntraAuthentication)
        {
            // Npgsql keeps using the token it fetched until the next refresh, while Azure.Identity only promises
            // that a token it returns is valid for a few more minutes (it refreshes cached tokens 5 minutes before
            // they expire). Refreshing more often than that margin means a new connection never presents an
            // expired token. It costs nothing: Azure.Identity answers from its in-memory cache until then.
            builder.UsePeriodicPasswordProvider(
                async (_, cancellationToken) =>
                {
                    var token = await credential.GetTokenAsync(new TokenRequestContext(EntraScopes), cancellationToken).ConfigureAwait(false);
                    return token.Token;
                },
                successRefreshInterval: EntraTokenRefreshInterval,
                failureRefreshInterval: TimeSpan.FromSeconds(10));
        }

        return builder.Build();
    }
}
