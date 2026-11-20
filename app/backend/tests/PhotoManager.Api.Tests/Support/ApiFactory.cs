extern alias migrator;

using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using NSubstitute;
using PhotoManager.Api.Contracts;
using PhotoManager.Data;
using PhotoManager.Infrastructure.Storage;
using Temporalio.Client;
using Testcontainers.PostgreSql;
using AppRole = migrator::PhotoManager.Migrator.AppRole;
using AppRoleKind = migrator::PhotoManager.Migrator.AppRoleKind;
using RoleProvisioner = migrator::PhotoManager.Migrator.RoleProvisioner;

namespace PhotoManager.Api.Tests.Support;

/// <summary>
/// Runs the real API pipeline against a throwaway PostgreSQL container. Only true external boundaries are
/// replaced: blob storage (in memory), Temporal (substitute) and GitHub OAuth (header-based test scheme).
/// The API connects as a web-tier role provisioned by the migrator's real grants, exactly as in production, so
/// every test also proves the web tier needs no privilege it does not have. Test setup and assertions use the
/// database owner (<see cref="WithDbAsync{T}"/>).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const long AdminId = 1001;
    public const long MemberId = 1002;
    public const long StrangerId = 9999;

    public const string WebRoleName = "id-photo-test-web";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string _webRolePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private NpgsqlDataSource? _ownerDataSource;

    public InMemoryBlobStore Blobs { get; } = new();

    public ITemporalClient Temporal { get; } = Substitute.For<ITemporalClient>();

    public string WebKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Gets the connection string of the least-privileged web-tier role the API runs as.</summary>
    public string WebConnectionString => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
    {
        Username = WebRoleName,
        Password = _webRolePassword,
    }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _ownerDataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        await using (var db = CreateOwnerContext())
        {
            await db.Database.MigrateAsync();
        }

        // Production roles are created from Entra identities; here a password login stands in for one, and the
        // provisioner (which only creates roles that are missing) applies the real grants to it.
        await using (var create = _ownerDataSource.CreateCommand($"CREATE ROLE \"{WebRoleName}\" LOGIN PASSWORD '{_webRolePassword}'"))
        {
            await create.ExecuteNonQueryAsync();
        }

        var database = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()).Database!;
        AppRole[] roles = [new AppRole { Name = WebRoleName, ObjectId = Guid.NewGuid(), Kind = AppRoleKind.Web }];
        await RoleProvisioner.ProvisionAsync(_ownerDataSource, _ownerDataSource, database, roles, CancellationToken.None);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_ownerDataSource is not null)
        {
            await _ownerDataSource.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    /// <summary>Creates an https client (so __Host-/Secure cookies round-trip) acting as the given GitHub user.</summary>
    public HttpClient ClientFor(long? gitHubId)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (gitHubId is { } id)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.Header, id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return client;
    }

    /// <summary>Signs a client up for CSRF: fetches the session (sets the antiforgery cookie) and adds the header.</summary>
    public static async Task<HttpClient> WithCsrfAsync(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<SessionResponse>("/api/auth/session", TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session!.CsrfToken);
        return client;
    }

    /// <summary>Runs test setup or assertions as the database owner (the API itself runs as the web role).</summary>
    public async Task<T> WithDbAsync<T>(Func<PhotoDbContext, Task<T>> action)
    {
        await using var db = CreateOwnerContext();
        return await action(db);
    }

    private PhotoDbContext CreateOwnerContext()
    {
        var options = new DbContextOptionsBuilder<PhotoDbContext>();
        PhotoDbContextOptions.Configure(options, _ownerDataSource!);
        return new PhotoDbContext(options.Options);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", WebConnectionString);
        builder.UseSetting("Database:UseEntraAuthentication", "false");
        builder.UseSetting("Crypto:WebKey", WebKey);
        builder.UseSetting("Temporal:PayloadKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Blob:ConnectionString", "UseDevelopmentStorage=true");
        builder.UseSetting("Auth:GitHubClientId", "test-client");
        builder.UseSetting("Auth:GitHubClientSecret", "test-secret");
        builder.UseSetting("Auth:Users:0:GitHubId", AdminId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Auth:Users:0:Role", "Admin");
        builder.UseSetting("Auth:Users:1:GitHubId", MemberId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Auth:Users:1:Role", "Member");
        builder.UseSetting("Uploads:MaxFileSizeBytes", "200000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBlobStore>();
            services.AddSingleton<IBlobStore>(Blobs);
            services.RemoveAll<ITemporalClient>();
            services.AddSingleton(Temporal);
            services.AddAuthentication(o =>
            {
                o.DefaultScheme = TestAuthHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
