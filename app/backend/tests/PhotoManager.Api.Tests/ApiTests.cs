using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using PhotoManager.Api.Auth;
using PhotoManager.Api.Contracts;
using PhotoManager.Api.Tests.Support;
using PhotoManager.Api.Uploads;
using PhotoManager.Core.Crypto;
using PhotoManager.Core.Domain;
using PhotoManager.Core.Workflows;
using PhotoManager.Data.Entities;
using PhotoManager.Infrastructure.Crypto;
using PhotoManager.Infrastructure.Storage;
using Temporalio.Client;

namespace PhotoManager.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class ApiTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_response_carries_browser_security_headers()
    {
        var response = await factory.ClientFor(null).GetAsync("/api/auth/session", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.False(response.Headers.Contains("Server"));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(ApiFactory.StrangerId, HttpStatusCode.Forbidden)]
    [InlineData(ApiFactory.MemberId, HttpStatusCode.OK)]
    public async Task Only_allowlisted_users_can_read_photos(long? user, HttpStatusCode expected)
    {
        var response = await factory.ClientFor(user).GetAsync("/api/photos", Ct);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Session_reports_anonymous_and_signed_in_users()
    {
        var anonymous = await factory.ClientFor(null).GetFromJsonAsync<SessionResponse>("/api/auth/session", Json, Ct);
        Assert.False(anonymous!.Authenticated);
        Assert.False(string.IsNullOrEmpty(anonymous.CsrfToken), "Signed-out visitors need a token to open an invite link.");
        Assert.False(anonymous.GoogleSignIn);

        var admin = await factory.ClientFor(ApiFactory.AdminId).GetFromJsonAsync<SessionResponse>("/api/auth/session", Json, Ct);
        Assert.True(admin!.Authenticated);
        Assert.Equal(PhotoRoles.Admin, admin.User!.Role);
        Assert.Equal(ApiFactory.AdminId, admin.User.Id);
        Assert.False(string.IsNullOrEmpty(admin.CsrfToken));
    }

    [Fact]
    public async Task Mutations_require_a_csrf_token_and_cookies_are_hardened()
    {
        var client = factory.ClientFor(ApiFactory.MemberId);
        var withoutToken = await client.PostAsync("/api/auth/logout", null, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, withoutToken.StatusCode);

        var session = await client.GetAsync("/api/auth/session", Ct);
        var setCookie = session.Headers.GetValues("Set-Cookie").Single(c => c.Contains("pm-csrf", StringComparison.Ordinal));
        Assert.StartsWith("__Host-pm-csrf=", setCookie, StringComparison.Ordinal);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);

        var token = (await session.Content.ReadFromJsonAsync<SessionResponse>(Json, Ct))!.CsrfToken;
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        var withToken = await client.PostAsync("/api/auth/logout", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, withToken.StatusCode);
        Assert.Equal("\"cache\"", withToken.Headers.GetValues("Clear-Site-Data").Single());
    }

    [Fact]
    public async Task Rejected_sessions_clear_browser_cache()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var scheme = new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        var expiredPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, ApiFactory.MemberId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Role, PhotoRoles.Member),
            new Claim(PhotoClaims.SignedInAt, "0"),
        ], CookieAuthenticationDefaults.AuthenticationScheme));
        var context = new CookieValidatePrincipalContext(httpContext, scheme, options, new AuthenticationTicket(expiredPrincipal, CookieAuthenticationDefaults.AuthenticationScheme));

        await options.Events.OnValidatePrincipal(context);

        Assert.Null(context.Principal);
        Assert.Equal("\"cache\"", httpContext.Response.Headers["Clear-Site-Data"].ToString());
    }

    [Fact]
    public async Task Uploads_can_skip_the_ai_description_and_keep_that_choice_on_retry()
    {
        var client = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. System.Security.Cryptography.RandomNumberGenerator.GetBytes(5000)];

        using var accepted = await client.PostAsync("/api/uploads?describe=false", FileContent("no-ai.jpg", jpeg), Ct);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var photoId = (await accepted.Content.ReadFromJsonAsync<UploadAccepted>(Json, Ct))!.PhotoId;
        Assert.Equal(ProcessingState.NotStarted, await factory.WithDbAsync(db => db.Photos.AsNoTracking().Where(p => p.Id == photoId).Select(p => p.DescriptionState).SingleAsync(Ct)));
        await factory.Temporal.Received(1).StartWorkflowAsync(
            WorkflowTypes.PhotoIngest,
            Arg.Is<IReadOnlyCollection<object?>>(a => a.OfType<PhotoIngestInput>().Any(i => i.PhotoId == photoId && i.SkipDescription)),
            Arg.Any<WorkflowOptions>());

        // A retry after a failed ingest makes the same choice.
        await factory.WithDbAsync(db => db.Photos.Where(p => p.Id == photoId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PhotoStatus.Failed), Ct));
        using var retried = await client.PostAsync($"/api/uploads/{photoId}/retry", null, Ct);
        Assert.Equal(HttpStatusCode.Accepted, retried.StatusCode);
        await factory.Temporal.Received(2).StartWorkflowAsync(
            WorkflowTypes.PhotoIngest,
            Arg.Is<IReadOnlyCollection<object?>>(a => a.OfType<PhotoIngestInput>().Any(i => i.PhotoId == photoId && i.SkipDescription)),
            Arg.Any<WorkflowOptions>());
    }

    [Fact]
    public async Task Upload_sniffs_content_enforces_size_and_stages_only_ciphertext()
    {
        var client = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));

        var notAnImage = await client.PostAsync("/api/uploads", FileContent("evil.jpg", "<script>alert(1)</script>"u8.ToArray()), Ct);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, notAnImage.StatusCode);

        var tooLarge = await client.PostAsync("/api/uploads", FileContent("big.jpg", [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[300_000]]), Ct);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);

        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. System.Security.Cryptography.RandomNumberGenerator.GetBytes(5000)];
        var accepted = await client.PostAsync("/api/uploads?batchId=" + Guid.NewGuid(), FileContent("../../holiday.jpeg", jpeg), Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<UploadAccepted>(Json, Ct);

        var staged = factory.Blobs.Blobs[(BlobArea.Staging, BlobNames.Staging(body!.PhotoId))];
        Assert.True(PhotoCipherV2.HasV2Magic(staged));
        Assert.DoesNotContain(jpeg[100..140], Chunks(staged));
        var photo = await factory.WithDbAsync(db => db.Photos.AsNoTracking().SingleAsync(p => p.Id == body.PhotoId, Ct));
        Assert.Equal("holiday.jpeg", photo.OriginalFileName);
        Assert.Equal("holiday.jpeg", photo.FileName); // until an AI description renames it (a worker's job)
        Assert.Equal(PhotoStatus.Queued, photo.Status);
        Assert.Equal("user1002", photo.UploadedBy);
        await factory.Temporal.Received().StartWorkflowAsync(
            WorkflowTypes.PhotoIngest,
            Arg.Any<IReadOnlyCollection<object?>>(),
            Arg.Is<WorkflowOptions>(o => o.Id == WorkflowIds.PhotoIngest(body.PhotoId) && o.TaskQueue == TaskQueues.Ingest));
    }

    [Fact]
    public async Task Upload_clamps_long_filenames_and_cleans_up_staging_blob_on_db_failure()
    {
        var client = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));

        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. System.Security.Cryptography.RandomNumberGenerator.GetBytes(2000)];
        var accepted = await client.PostAsync("/api/uploads", FileContent(new string('a', 220), jpeg), Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<UploadAccepted>(Json, Ct);
        var photo = await factory.WithDbAsync(db => db.Photos.AsNoTracking().SingleAsync(p => p.Id == body!.PhotoId, Ct));
        Assert.Equal(FileNames.MaxLength, photo.OriginalFileName.Length);
        Assert.EndsWith(".jpg", photo.OriginalFileName, StringComparison.Ordinal);

        // If SaveChangesAsync fails after the staging blob is written, the blob is deleted instead of orphaned.
        var stagingCountBefore = factory.Blobs.Blobs.Keys.Count(k => k.Area == BlobArea.Staging);
        await using var scope = factory.Services.CreateAsyncScope();
        var uploader = scope.ServiceProvider.GetRequiredService<UploadService>();
        byte[] failingJpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. System.Security.Cryptography.RandomNumberGenerator.GetBytes(2000)];
        using var stream = new MemoryStream(failingJpeg);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            uploader.StageAsync(stream, "valid.jpg", null, new string('u', 150), ApiFactory.MemberId, describe: true, Ct));
        var stagingCountAfter = factory.Blobs.Blobs.Keys.Count(k => k.Area == BlobArea.Staging);
        Assert.Equal(stagingCountBefore, stagingCountAfter);
    }

    [Fact]
    public async Task Gallery_supports_full_text_search_and_keyset_paging()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await SeedReadyPhotoAsync($"zz{tag} woman and boy smiling on the beach", $"IMG_{tag}_1.jpg");
        await SeedReadyPhotoAsync($"zz{tag} dog running in the snow", $"IMG_{tag}_2.jpg");
        await SeedReadyPhotoAsync(null, $"mountain_{tag}_sunrise.jpg");
        var client = factory.ClientFor(ApiFactory.MemberId);

        var stemmed = await client.GetFromJsonAsync<PhotoPage>($"/api/photos?q=smile zz{tag}", Json, Ct);
        Assert.Single(stemmed!.Items);

        var fileNameWords = await client.GetFromJsonAsync<PhotoPage>($"/api/photos?q=sunrise {tag}", Json, Ct);
        Assert.Single(fileNameWords!.Items);

        var first = await client.GetFromJsonAsync<PhotoPage>($"/api/photos?q=zz{tag}&limit=1", Json, Ct);
        var second = await client.GetFromJsonAsync<PhotoPage>($"/api/photos?q=zz{tag}&limit=1&cursor={first!.NextCursor}", Json, Ct);
        Assert.NotEqual(first.Items[0].Id, second!.Items[0].Id);
        Assert.Null(second.NextCursor);

        var badCursor = await client.GetAsync("/api/photos?cursor=not-a-cursor", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, badCursor.StatusCode);
    }

    [Fact]
    public async Task Renditions_are_decrypted_on_the_fly_or_requested_from_a_worker()
    {
        var ready = await SeedReadyPhotoAsync("ready", "ready.jpg", ProcessingState.Ready);
        var crypto = factory.Services.GetRequiredService<PhotoCryptoService>();
        var webp = "RIFF\0\0\0\0WEBPVP8 fake"u8.ToArray();
        await factory.Blobs.UploadAsync(BlobArea.Renditions, BlobNames.Thumbnail(ready), await crypto.EncryptWithWebKeyAsync(webp, Ct), Ct);
        var client = factory.ClientFor(ApiFactory.MemberId);

        var served = await client.GetAsync($"/api/photos/{ready}/thumbnail?v=1", Ct);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/webp", served.Content.Headers.ContentType!.MediaType);
        Assert.Equal(webp, await served.Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("immutable", served.Headers.CacheControl!.ToString(), StringComparison.Ordinal);

        var unversioned = await client.GetAsync($"/api/photos/{ready}/thumbnail", Ct);
        Assert.Equal(HttpStatusCode.OK, unversioned.StatusCode);
        Assert.Equal("no-cache, private", unversioned.Headers.CacheControl!.ToString());

        var mismatched = await client.GetAsync($"/api/photos/{ready}/thumbnail?v=99", Ct);
        Assert.Equal(HttpStatusCode.OK, mismatched.StatusCode);
        Assert.Equal("no-cache, private", mismatched.Headers.CacheControl!.ToString());

        var goneBlob = await SeedReadyPhotoAsync("gone", "gone.jpg", ProcessingState.Ready);
        var regenerate = await client.GetAsync($"/api/photos/{goneBlob}/thumbnail?v=1", Ct);
        Assert.Equal(HttpStatusCode.Accepted, regenerate.StatusCode);
        var resetState = await factory.WithDbAsync(db => db.Photos.AsNoTracking().Where(p => p.Id == goneBlob).Select(p => p.RenditionState).SingleAsync(Ct));
        Assert.Equal(ProcessingState.NotStarted, resetState);
        await factory.Temporal.Received().StartWorkflowAsync(WorkflowTypes.EnsureRenditions, Arg.Any<IReadOnlyCollection<object?>>(), Arg.Is<WorkflowOptions>(o => o.Id == WorkflowIds.EnsureRenditions(goneBlob)));

        var missing = await SeedReadyPhotoAsync("legacy", "legacy.jpg", ProcessingState.NotStarted);
        var pending = await client.GetAsync($"/api/photos/{missing}/thumbnail", Ct);
        Assert.Equal(HttpStatusCode.Accepted, pending.StatusCode);
        await factory.Temporal.Received().StartWorkflowAsync(WorkflowTypes.EnsureRenditions, Arg.Any<IReadOnlyCollection<object?>>(), Arg.Is<WorkflowOptions>(o => o.Id == WorkflowIds.EnsureRenditions(missing)));
    }

    [Fact]
    public async Task Maintenance_actions_are_admin_only()
    {
        var member = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));
        var denied = await member.PostAsJsonAsync("/api/jobs", new StartJobRequest(JobKind.ReindexArchive), Json, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var admin = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.AdminId));
        var started = await admin.PostAsJsonAsync("/api/jobs", new StartJobRequest(JobKind.ReindexArchive), Json, Ct);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        await factory.Temporal.Received().StartWorkflowAsync(WorkflowTypes.ReindexArchive, Arg.Any<IReadOnlyCollection<object?>>(), Arg.Is<WorkflowOptions>(o => o.Id == WorkflowIds.ReindexArchive));
    }

    [Fact]
    public async Task Downloads_are_private_to_their_requester()
    {
        var id = Guid.CreateVersion7();
        await factory.WithDbAsync(async db =>
        {
            // Ownership follows the immutable GitHub id. The stored login is deliberately the *member's* login (as if
            // the admin had renamed their account and the member had taken over the old name): it must not matter.
            db.Downloads.Add(new PreparedDownload { Id = id, Status = DownloadStatus.Ready, PhotoIds = [Guid.NewGuid()], RequestedBy = "user1002", RequestedById = ApiFactory.AdminId, FileName = "x.jpg", BlobName = "x", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
            return await db.SaveChangesAsync(Ct);
        });

        Assert.Equal(HttpStatusCode.NotFound, (await factory.ClientFor(ApiFactory.MemberId).GetAsync($"/api/downloads/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.ClientFor(ApiFactory.AdminId).GetAsync($"/api/downloads/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Downloads_enforce_raw_count_size_and_pending_concurrency_limits()
    {
        var kestrel = factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        Assert.Equal(64 * 1024, kestrel.Limits.MaxRequestBodySize);

        var smallId = await SeedReadyPhotoAsync("small", "small.jpg", sizeBytes: 1024);
        var hugeA = await SeedReadyPhotoAsync("huge-a", "huge-a.jpg", sizeBytes: 2_500_000_000L);
        var hugeB = await SeedReadyPhotoAsync("huge-b", "huge-b.jpg", sizeBytes: 2_500_000_000L);

        var member = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));

        // 501 duplicate GUIDs must be rejected before deduplication collapses them to 1.
        var tooManyDuplicates = await member.PostAsJsonAsync(
            "/api/downloads",
            new CreateDownloadRequest([.. Enumerable.Repeat(smallId, 501)]),
            Json,
            Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooManyDuplicates.StatusCode);

        // Aggregate size above 4 GiB is rejected upfront before queuing a worker job.
        var tooLarge = await member.PostAsJsonAsync(
            "/api/downloads",
            new CreateDownloadRequest([hugeA, hugeB]),
            Json,
            Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);

        // Expired pending downloads do not count against the active concurrency cap.
        await factory.WithDbAsync(async db =>
        {
            db.Downloads.Add(new PreparedDownload
            {
                Id = Guid.CreateVersion7(),
                Status = DownloadStatus.Pending,
                PhotoIds = [smallId],
                RequestedBy = "user1002",
                RequestedById = ApiFactory.MemberId,
                FileName = "expired.jpg",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            });
            return await db.SaveChangesAsync(Ct);
        });

        // At most 3 active pending downloads per user; a 4th is rejected with 429 while another user is unaffected.
        var createdIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var accepted = await member.PostAsJsonAsync("/api/downloads", new CreateDownloadRequest([smallId]), Json, Ct);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            var body = await accepted.Content.ReadFromJsonAsync<DownloadResponse>(Json, Ct);
            createdIds.Add(body!.Id);
        }

        var fourth = await member.PostAsJsonAsync("/api/downloads", new CreateDownloadRequest([smallId]), Json, Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);

        var admin = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.AdminId));
        var adminAccepted = await admin.PostAsJsonAsync("/api/downloads", new CreateDownloadRequest([smallId]), Json, Ct);
        Assert.Equal(HttpStatusCode.Accepted, adminAccepted.StatusCode);

        // Clean up pending rows so later tests for MemberId are unaffected.
        var adminBody = await adminAccepted.Content.ReadFromJsonAsync<DownloadResponse>(Json, Ct);
        createdIds.Add(adminBody!.Id);
        await factory.WithDbAsync(db => db.Downloads.Where(d => createdIds.Contains(d.Id)).ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DownloadStatus.Ready), Ct));
    }

    [Fact]
    public async Task Browser_style_multipart_uploads_are_accepted()
    {
        // Browsers always quote the name and filename parameters and never send filename*; HttpClient does the
        // opposite, so the body is built by hand to cover what real browsers send.
        var client = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));
        const string Boundary = "----WebKitFormBoundary7MA4YWxkTrZu0gW";
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. System.Security.Cryptography.RandomNumberGenerator.GetBytes(4000)];
        var head = System.Text.Encoding.UTF8.GetBytes($"--{Boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"Beach day.jpg\"\r\nContent-Type: image/jpeg\r\n\r\n");
        var tail = System.Text.Encoding.UTF8.GetBytes($"\r\n--{Boundary}--\r\n");
        using var content = new ByteArrayContent([.. head, .. jpeg, .. tail]);
        content.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={Boundary}");

        var response = await client.PostAsync("/api/uploads", content, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UploadAccepted>(Json, Ct);
        var photo = await factory.WithDbAsync(db => db.Photos.AsNoTracking().SingleAsync(p => p.Id == body!.PhotoId, Ct));
        Assert.Equal("Beach day.jpg", photo.OriginalFileName);
        Assert.Equal(ApiFactory.MemberId, photo.UploadedById);
    }

    [Fact]
    public async Task Unfinished_photos_are_not_visible_or_editable_by_anyone()
    {
        var id = await SeedReadyPhotoAsync(null, "processing.jpg");
        await factory.WithDbAsync(db => db.Photos.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PhotoStatus.Processing), Ct));
        var client = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/photos/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PatchAsJsonAsync($"/api/photos/{id}", new { description = "mine now" }, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/photos/{id}/thumbnail?v=1", Ct)).StatusCode);

        // The unused "regenerate renditions" endpoint was removed.
        var ready = await SeedReadyPhotoAsync(null, "ready.jpg");
        Assert.Contains((await client.PostAsync($"/api/photos/{ready}/renditions", null, Ct)).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }

    [Fact]
    public async Task Search_rejects_control_characters_instead_of_failing()
    {
        var response = await factory.ClientFor(ApiFactory.MemberId).GetAsync("/api/photos?q=a%00b", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Describe_request_is_rolled_back_when_temporal_is_unreachable()
    {
        var id = await SeedReadyPhotoAsync("Old words.", "x.jpg");
        factory.Temporal.StartWorkflowAsync(WorkflowTypes.DescribePhoto, Arg.Any<IReadOnlyCollection<object?>>(), Arg.Is<WorkflowOptions>(o => o.Id == WorkflowIds.DescribePhoto(id)))
            .Returns<Task<WorkflowHandle>>(_ => throw new InvalidOperationException("Temporal is unreachable"));
        var client = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));

        var response = await client.PostAsync($"/api/photos/{id}/describe", null, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var photo = await factory.WithDbAsync(db => db.Photos.AsNoTracking().SingleAsync(p => p.Id == id, Ct));
        Assert.Equal(ProcessingState.NotStarted, photo.DescriptionState);
    }

    [Fact]
    public async Task Job_that_cannot_start_is_recorded_as_failed_not_left_queued()
    {
        factory.Temporal.StartWorkflowAsync(WorkflowTypes.ReindexArchive, Arg.Is<IReadOnlyCollection<object?>>(a => a.OfType<ReindexArchiveInput>().Any(i => i.Force)), Arg.Any<WorkflowOptions>())
            .Returns<Task<WorkflowHandle>>(_ => throw new InvalidOperationException("Temporal is unreachable"));
        var admin = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.AdminId));

        var response = await admin.PostAsJsonAsync("/api/jobs", new StartJobRequest(JobKind.ReindexArchive, Force: true), Json, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var job = await factory.WithDbAsync(db => db.Jobs.AsNoTracking().Where(j => j.Kind == JobKind.ReindexArchive).OrderByDescending(j => j.CreatedAt).FirstAsync(Ct));
        Assert.Equal(JobStatus.Failed, job.Status);
    }

    [Fact]
    public async Task Description_edits_are_validated_sanitized_and_attributed()
    {
        var id = await SeedReadyPhotoAsync("old", "a.jpg");
        var client = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));

        var tooLong = await client.PatchAsJsonAsync($"/api/photos/{id}", new UpdatePhotoRequest(new string('a', 301)), Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var ok = await client.PatchAsJsonAsync($"/api/photos/{id}", new UpdatePhotoRequest("  family picnic\n‮ "), Json, Ct);
        var details = await ok.Content.ReadFromJsonAsync<PhotoDetails>(Json, Ct);
        Assert.Equal("family picnic", details!.Description);
        Assert.Equal(DescriptionSource.User, details.DescriptionSource);
    }

    [Theory]
    [InlineData("/uploads", "/uploads")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData(null, "/")]
    public void Return_urls_cannot_redirect_off_site(string? input, string expected) =>
        Assert.Equal(expected, ReturnUrl.Sanitize(input));

    [Fact]
    public void Web_tier_refuses_to_start_with_the_archive_password()
    {
        using var misconfigured = factory.WithWebHostBuilder(b => b.UseSetting("Crypto:ArchivePassword", "should-never-be-here"));
        var error = Assert.Throws<InvalidOperationException>(() => misconfigured.CreateClient());
        Assert.Contains("ArchivePassword", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csrf_token_is_only_taken_from_the_header_and_bodies_are_not_read_without_it()
    {
        var client = factory.ClientFor(ApiFactory.MemberId);
        var session = await client.GetFromJsonAsync<SessionResponse>("/api/auth/session", Ct);

        // A valid token in a form field is what antiforgery would accept by default, by reading the whole form (for
        // an upload: buffering the photo to a temporary file). It is ignored: no header, no request.
        using var form = FileContent("a.jpg", new byte[4096]);
        form.Add(new StringContent(session!.CsrfToken!), "__RequestVerificationToken");
        using var response = await client.PostAsync("/api/uploads", form, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(AntiforgeryValidationFilter.ProblemType, problem.RootElement.GetProperty("type").GetString());

        // A malformed multipart body without the header is refused the same way, not a server error.
        using var malformed = new StringContent("--x\r\nnot a part");
        malformed.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=x");
        using var refused = await client.PostAsync("/api/uploads", malformed, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Sign_in_cookies_meet_the_browser_rules_for_host_prefixed_cookies()
    {
        // Browsers silently discard a __Host- cookie unless it is Secure, has Path=/ and no Domain. The correlation
        // cookie used to get Path=/signin-github, so production sign-ins failed with "Correlation failed".
        var client = factory.ClientFor(null);
        using var response = await client.GetAsync("/api/auth/login?returnUrl=%2Fgallery", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://github.com/login/oauth/authorize", response.Headers.Location?.AbsoluteUri, StringComparison.Ordinal);
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.StartsWith("__Host-pm-correlation", StringComparison.Ordinal));
        Assert.All(cookies.Where(c => c.StartsWith("__Host-", StringComparison.Ordinal)), cookie =>
        {
            var attributes = cookie.Split(';').Skip(1).Select(a => a.Trim()).ToList();
            Assert.Contains("path=/", attributes, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("secure", attributes, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(attributes, a => a.StartsWith("domain=", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public async Task OAuth_callback_failures_are_reported_as_failed_and_rate_limited()
    {
        // One test, because the callback's per-IP limit is shared by every request in this test host.
        var client = factory.ClientFor(null);
        var responses = new List<(HttpStatusCode Status, string? Location)>();
        for (var i = 0; i < 31; i++)
        {
            using var response = await client.GetAsync($"{AuthenticationSetup.CallbackPath}?error=access_denied&state=x", Ct);
            responses.Add((response.StatusCode, response.Headers.Location?.OriginalString));
        }

        // A callback GitHub did not complete (here: no valid state) is a failed sign-in, never "account not
        // allowed": only the allowlist check sends people to the denied page.
        Assert.Equal((HttpStatusCode.Redirect, AuthenticationSetup.FailedRedirect), responses[0]);

        // The authentication middleware answers the callback, yet the 31st within a minute is refused.
        Assert.All(responses.Take(30), r => Assert.NotEqual(HttpStatusCode.TooManyRequests, r.Status));
        Assert.Equal(HttpStatusCode.TooManyRequests, responses[^1].Status);
    }

    [Fact]
    public async Task Job_start_succeeds_when_the_worker_records_the_run_first()
    {
        // The worker may mark the job running (recording its run id) before the API writes the run id itself. That
        // used to fail the request with a concurrency error after the job had already started, skipping the audit.
        factory.Temporal.StartWorkflowAsync(
                WorkflowTypes.Backfill,
                Arg.Is<IReadOnlyCollection<object?>>(a => a.OfType<BackfillInput>().Any(i => i.Kind == JobKind.BackfillDescriptions)),
                Arg.Any<WorkflowOptions>())
            .Returns(async call =>
            {
                var input = call.ArgAt<IReadOnlyCollection<object?>>(1).OfType<BackfillInput>().Single();
                await factory.WithDbAsync(db => db.Jobs.Where(j => j.Id == input.JobId).ExecuteUpdateAsync(
                    s => s.SetProperty(j => j.Status, JobStatus.Running).SetProperty(j => j.RunId, "run-from-worker"),
                    Ct));
                return new WorkflowHandle(factory.Temporal, WorkflowIds.Backfill(JobKind.BackfillDescriptions), FirstExecutionRunId: "run-from-worker");
            });
        var admin = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.AdminId));

        using var response = await admin.PostAsJsonAsync("/api/jobs", new StartJobRequest(JobKind.BackfillDescriptions), Json, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await factory.WithDbAsync(db => db.Jobs.AsNoTracking().Where(j => j.Kind == JobKind.BackfillDescriptions).OrderByDescending(j => j.CreatedAt).FirstAsync(Ct));
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal("run-from-worker", job.RunId);
        Assert.True(await factory.WithDbAsync(db => db.AuditEvents.AnyAsync(e => e.Action == "job.start" && e.Target == job.Id.ToString(), Ct)));
    }

    [Fact]
    public async Task Cancelling_a_job_without_a_recorded_run_never_targets_its_workflow_id()
    {
        // Job workflow ids are reused, so a cancel without a run id would stop whichever job runs under the id now.
        var jobId = Guid.CreateVersion7();
        var workflowId = $"test-unstarted-{jobId:N}";
        await factory.WithDbAsync(async db =>
        {
            db.Jobs.Add(new MaintenanceJob { Id = jobId, Kind = JobKind.BackfillRenditions, Status = JobStatus.Queued, WorkflowId = workflowId, RequestedBy = "user1001" });
            return await db.SaveChangesAsync(Ct);
        });
        var admin = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.AdminId));

        using var response = await admin.PostAsync($"/api/jobs/{jobId}/cancel", null, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        factory.Temporal.DidNotReceive().GetWorkflowHandle(workflowId, Arg.Any<string?>(), Arg.Any<string?>());
        var job = await factory.WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId, Ct));
        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public async Task Timestamps_with_a_time_zone_offset_are_stored_as_utc()
    {
        // Npgsql refuses to write a DateTimeOffset whose offset is not zero; the context normalizes every value.
        var id = Guid.CreateVersion7();
        var takenAt = new DateTimeOffset(2024, 7, 4, 18, 22, 1, TimeSpan.FromHours(-4));
        await factory.WithDbAsync(async db =>
        {
            db.Photos.Add(new Photo { Id = id, Source = PhotoSource.Archive, Status = PhotoStatus.Ready, EncryptionFormat = EncryptionFormat.LegacyV1, ArchiveKey = $"2024-07-04/{id:N}.jpg.encrypted", OriginalFileName = "offset.jpg", FileName = "offset.jpg", ContentType = "image/jpeg", UploadedAt = DateTimeOffset.UtcNow, TakenAt = takenAt });
            return await db.SaveChangesAsync(Ct);
        });

        var stored = await factory.WithDbAsync(db => db.Photos.AsNoTracking().Where(p => p.Id == id).Select(p => p.TakenAt).SingleAsync(Ct));

        Assert.Equal(takenAt, stored); // the same instant
        Assert.Equal(TimeSpan.Zero, stored!.Value.Offset);
    }

    [Fact]
    public async Task Web_database_role_cannot_point_a_photo_at_another_archive_object()
    {
        var id = await SeedReadyPhotoAsync(null, "web-role.jpg");
        await using var connection = new NpgsqlConnection(factory.WebConnectionString);
        await connection.OpenAsync(Ct);

        // What the web tier legitimately changes...
        await using (var edit = new NpgsqlCommand("UPDATE photos SET description = 'edited', updated_at = now() WHERE id = @id", connection))
        {
            edit.Parameters.AddWithValue("id", id);
            Assert.Equal(1, await edit.ExecuteNonQueryAsync(Ct));
        }

        // ...but never where a photo is archived, how it is encrypted, its content hash or who uploaded it, which
        // would let a compromised web tier have a worker decrypt any object in the bucket for it.
        string[] tampering =
        [
            "UPDATE photos SET archive_key = 'v2/2026/11/00000000000000000000000000000000.pmv2' WHERE id = @id",
            "UPDATE photos SET encryption_format = 'V2' WHERE id = @id",
            "UPDATE photos SET sha256 = repeat('0', 64) WHERE id = @id",
            "UPDATE photos SET uploaded_by_id = 1 WHERE id = @id",
            "DELETE FROM photos WHERE id = @id",
        ];
        foreach (var sql in tampering)
        {
            await using var tamper = new NpgsqlCommand(sql, connection);
            tamper.Parameters.AddWithValue("id", id);
            var error = await Assert.ThrowsAsync<PostgresException>(() => tamper.ExecuteNonQueryAsync(Ct));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
    }

    private static MultipartFormDataContent FileContent(string fileName, byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static IEnumerable<byte[]> Chunks(byte[] data)
    {
        for (var i = 0; i + 40 <= data.Length; i++)
        {
            yield return data[i..(i + 40)];
        }
    }

    private Task<Guid> SeedReadyPhotoAsync(string? description, string fileName, ProcessingState renditions = ProcessingState.Ready, long? sizeBytes = null) =>
        factory.WithDbAsync(async db =>
        {
            var photo = new Photo
            {
                Id = Guid.CreateVersion7(),
                Source = PhotoSource.Archive,
                Status = PhotoStatus.Ready,
                EncryptionFormat = EncryptionFormat.LegacyV1,
                ArchiveKey = $"2024-01-01/{Guid.NewGuid():N}.jpg.encrypted",
                OriginalFileName = fileName,
                FileName = fileName,
                ContentType = "image/jpeg",
                SizeBytes = sizeBytes,
                Description = description,
                UploadedAt = DateTimeOffset.UtcNow,
                RenditionState = renditions,
                RenditionVersion = 1,
            };
            db.Photos.Add(photo);
            await db.SaveChangesAsync(Ct);
            return photo.Id;
        });
}
