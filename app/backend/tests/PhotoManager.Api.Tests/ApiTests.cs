using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PhotoManager.Api.Auth;
using PhotoManager.Api.Contracts;
using PhotoManager.Api.Tests.Support;
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
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
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
        Assert.Null(anonymous.CsrfToken);

        var admin = await factory.ClientFor(ApiFactory.AdminId).GetFromJsonAsync<SessionResponse>("/api/auth/session", Json, Ct);
        Assert.True(admin!.Authenticated);
        Assert.Equal(PhotoRoles.Admin, admin.User!.Role);
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
        Assert.Equal(PhotoStatus.Queued, photo.Status);
        Assert.Equal("user1002", photo.UploadedBy);
        await factory.Temporal.Received().StartWorkflowAsync(
            WorkflowTypes.PhotoIngest,
            Arg.Any<IReadOnlyCollection<object?>>(),
            Arg.Is<WorkflowOptions>(o => o.Id == WorkflowIds.PhotoIngest(body.PhotoId) && o.TaskQueue == TaskQueues.Ingest));
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

    private Task<Guid> SeedReadyPhotoAsync(string? description, string fileName, ProcessingState renditions = ProcessingState.Ready) =>
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
                ContentType = "image/jpeg",
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
