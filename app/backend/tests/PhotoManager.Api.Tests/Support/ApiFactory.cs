using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using PhotoManager.Api.Contracts;
using PhotoManager.Data;
using PhotoManager.Infrastructure.Storage;
using Temporalio.Client;
using Testcontainers.PostgreSql;

namespace PhotoManager.Api.Tests.Support;

/// <summary>
/// Runs the real API pipeline against a throwaway PostgreSQL container. Only true external boundaries are
/// replaced: blob storage (in memory), Temporal (substitute) and GitHub OAuth (header-based test scheme).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const long AdminId = 1001;
    public const long MemberId = 1002;
    public const long StrangerId = 9999;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public InMemoryBlobStore Blobs { get; } = new();

    public ITemporalClient Temporal { get; } = Substitute.For<ITemporalClient>();

    public string WebKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PhotoDbContext>().Database.MigrateAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
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

    public async Task<T> WithDbAsync<T>(Func<PhotoDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<PhotoDbContext>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", _postgres.GetConnectionString());
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
