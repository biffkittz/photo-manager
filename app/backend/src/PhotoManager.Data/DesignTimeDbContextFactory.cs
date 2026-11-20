using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace PhotoManager.Data;

/// <summary>
/// Used only by <c>dotnet ef</c> to create migrations. Points at the local docker-compose database unless
/// <c>PHOTOMANAGER_DESIGN_CONNECTION</c> is set. Never used at runtime.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PhotoDbContext>
{
    /// <inheritdoc />
    public PhotoDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PHOTOMANAGER_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=55432;Database=photomanager;Username=photomanager;Password=photomanager-dev";
        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        var options = PhotoDbContextOptions.Configure(new DbContextOptionsBuilder<PhotoDbContext>(), dataSource);
        return new PhotoDbContext((DbContextOptions<PhotoDbContext>)options.Options);
    }
}
