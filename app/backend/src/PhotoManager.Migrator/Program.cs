using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PhotoManager.Data;
using PhotoManager.Infrastructure.Azure;
using PhotoManager.Infrastructure.Configuration;
using PhotoManager.Infrastructure.Database;
using PhotoManager.Migrator;

// Applies EF Core migrations and least-privilege role grants, then exits. In Azure it runs as a Container Apps
// Job inside the VNet (the database has no public endpoint) using the migrator identity, which is the server's
// Microsoft Entra administrator. CI/CD starts it before rolling out new app revisions.
// Content root = the app directory, so appsettings*.json are found regardless of the caller's working directory.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
var credential = AzureCredentialFactory.Create(builder.Configuration);
builder.Services.AddSingleton(credential);
builder.Services.AddPhotoDatabase(builder.Configuration, credential);

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Migrator");
using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(15));

await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PhotoDbContext>();
    var pending = (await db.Database.GetPendingMigrationsAsync(cancellation.Token)).ToList();
    logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
    await db.Database.MigrateAsync(cancellation.Token);
}

var roles = builder.Configuration.GetSection("Migrator:AppRoles").Get<List<AppRole>>() ?? [];
if (roles.Count > 0)
{
    var options = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()!;
    var appConnection = new NpgsqlConnectionStringBuilder(options.ConnectionString);
    var adminConnection = new NpgsqlConnectionStringBuilder(options.ConnectionString) { Database = "postgres" };

    await using var appDataSource = DatabaseServiceCollectionExtensions.BuildDataSource(options, credential);
    await using var adminDataSource = DatabaseServiceCollectionExtensions.BuildDataSource(
        new DatabaseOptions { ConnectionString = adminConnection.ConnectionString, UseEntraAuthentication = options.UseEntraAuthentication },
        credential);

    await RoleProvisioner.ProvisionAsync(adminDataSource, appDataSource, appConnection.Database!, roles, cancellation.Token);
    logger.LogInformation("Provisioned {Count} application role(s)", roles.Count);
}

logger.LogInformation("Database is up to date");
