using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PhotoManager.Api.Contracts;
using PhotoManager.Api.Tests.Support;
using PhotoManager.Core.Domain;

namespace PhotoManager.Api.Tests;

/// <summary>Albums: shared by everyone signed in, only library photos, nothing ever deleted (docs/adr/0013).</summary>
[Collection(ApiCollection.Name)]
public sealed class AlbumTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static readonly DateTimeOffset July1 = new(2019, 7, 1, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Albums_are_shared_by_everyone_and_list_library_photos_oldest_first()
    {
        var (older, newer, duplicate) = await SeedThreeAsync();
        var member = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));

        var created = await member.PostAsJsonAsync("/api/albums", new CreateAlbumRequest("  Beach 2019 ", [newer, older, duplicate, Guid.NewGuid()]), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var album = (await created.Content.ReadFromJsonAsync<AlbumSummary>(Json, Ct))!;
        Assert.Equal("Beach 2019", album.Name);
        Assert.Equal(2, album.PhotoCount); // the hidden duplicate and the unknown id are left out
        Assert.Equal(July1, album.FirstDate);
        Assert.Equal(July1.AddDays(4), album.LastDate);
        Assert.Equal(older, album.Cover!.Id); // the first photo until a cover is chosen

        // Everyone signed in sees it (an admin here) and pages through its photos, oldest first.
        var admin = factory.ClientFor(ApiFactory.AdminId);
        var albums = await admin.GetFromJsonAsync<List<AlbumSummary>>("/api/albums", Json, Ct);
        Assert.Contains(albums!, a => a.Id == album.Id);
        var first = await admin.GetFromJsonAsync<PhotoPage>($"/api/albums/{album.Id}/photos?limit=1", Json, Ct);
        Assert.Equal(older, Assert.Single(first!.Items).Id);
        var second = await admin.GetFromJsonAsync<PhotoPage>($"/api/albums/{album.Id}/photos?limit=1&cursor={first.NextCursor}", Json, Ct);
        Assert.Equal(newer, Assert.Single(second!.Items).Id);
        Assert.Null(second.NextCursor);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.ClientFor(null).GetAsync("/api/albums", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.ClientFor(ApiFactory.StrangerId).GetAsync($"/api/albums/{album.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.ClientFor(ApiFactory.MemberId).PostAsJsonAsync("/api/albums", new CreateAlbumRequest("No CSRF token", null), Json, Ct)).StatusCode);

        var creator = await factory.WithDbAsync(db => db.Albums.AsNoTracking().Where(a => a.Id == album.Id).Select(a => a.CreatedById).SingleAsync(Ct));
        Assert.Equal(ApiFactory.MemberId, creator);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tab\tin the name")]
    [InlineData("a name that goes on and on and on and on and on and on and on and on and on and on and on and on and on")]
    public async Task Album_names_are_validated(string name)
    {
        var member = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));
        var response = await member.PostAsJsonAsync("/api/albums", new CreateAlbumRequest(name, null), Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Photo_lists_are_limited_before_duplicates_are_removed()
    {
        var photo = await Seed.PhotoAsync(factory, new PhotoSeed());
        var member = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));
        var album = await CreateAsync(member, "Limits", []);

        var tooMany = await member.PostAsJsonAsync($"/api/albums/{album.Id}/photos", new AlbumPhotosRequest([.. Enumerable.Repeat(photo, 501)]), Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        var none = await member.PostAsJsonAsync($"/api/albums/{album.Id}/photos", new AlbumPhotosRequest([]), Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        var tooManyOnCreate = await member.PostAsJsonAsync("/api/albums", new CreateAlbumRequest("Too many", [.. Enumerable.Repeat(photo, 501)]), Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooManyOnCreate.StatusCode);
        var unknown = await member.PostAsJsonAsync($"/api/albums/{Guid.NewGuid()}/photos", new AlbumPhotosRequest([photo]), Json, Ct);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // Adding the same photo twice keeps one copy.
        Assert.Equal(1, (await AddAsync(member, album.Id, [photo, photo])).PhotoCount);
        Assert.Equal(1, (await AddAsync(member, album.Id, [photo])).PhotoCount);
    }

    [Fact]
    public async Task Covers_must_be_in_the_album_and_fall_back_to_the_first_photo()
    {
        var (older, newer, _) = await SeedThreeAsync();
        var outsider = await Seed.PhotoAsync(factory, new PhotoSeed());
        var member = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));
        var album = await CreateAsync(member, "Covers", [older, newer]);

        var notInAlbum = await member.PatchAsJsonAsync($"/api/albums/{album.Id}", new UpdateAlbumRequest(null, outsider), Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, notInAlbum.StatusCode);

        var chosen = await member.PatchAsJsonAsync($"/api/albums/{album.Id}", new UpdateAlbumRequest("Covers, renamed", newer), Json, Ct);
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        var updated = (await chosen.Content.ReadFromJsonAsync<AlbumSummary>(Json, Ct))!;
        Assert.Equal("Covers, renamed", updated.Name);
        Assert.Equal(newer, updated.Cover!.Id);

        // Removing the cover photo falls back to the first photo, and the choice is forgotten: adding it back later
        // does not make it the cover again.
        Assert.Equal(older, (await RemoveAsync(member, album.Id, [newer])).Cover!.Id);
        Assert.Equal(older, (await AddAsync(member, album.Id, [newer])).Cover!.Id);
    }

    [Fact]
    public async Task Removing_photos_and_deleting_albums_only_record_when_it_happened()
    {
        var (older, newer, _) = await SeedThreeAsync();
        var member = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));
        var album = await CreateAsync(member, "Soft", [older, newer]);

        var afterRemove = await RemoveAsync(member, album.Id, [older]);
        Assert.Equal(1, afterRemove.PhotoCount);
        var removedAt = await factory.WithDbAsync(db => db.AlbumPhotos.AsNoTracking().Where(ap => ap.AlbumId == album.Id && ap.PhotoId == older).Select(ap => ap.RemovedAt).SingleAsync(Ct));
        Assert.NotNull(removedAt);
        Assert.Equal(2, (await AddAsync(member, album.Id, [older])).PhotoCount);

        // Anyone can delete an album (an admin here, created by a member); the photos stay in the library.
        var admin = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.AdminId));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/albums/{album.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/albums/{album.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/albums/{album.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/albums/{album.Id}/photos", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsJsonAsync($"/api/albums/{album.Id}/photos", new AlbumPhotosRequest([older]), Json, Ct)).StatusCode);
        Assert.DoesNotContain((await member.GetFromJsonAsync<List<AlbumSummary>>("/api/albums", Json, Ct))!, a => a.Id == album.Id);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/photos/{older}", Ct)).StatusCode);

        var kept = await factory.WithDbAsync(db => db.Albums.AsNoTracking().Where(a => a.Id == album.Id).Select(a => new { a.DeletedAt, Photos = a.Photos.Count }).SingleAsync(Ct));
        Assert.NotNull(kept.DeletedAt);
        Assert.Equal(2, kept.Photos);
    }

    [Fact]
    public async Task Web_database_role_cannot_delete_albums_or_rewrite_their_history()
    {
        var photo = await Seed.PhotoAsync(factory, new PhotoSeed());
        var member = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));
        var album = await CreateAsync(member, "Locked", [photo]);
        await using var connection = new NpgsqlConnection(factory.WebConnectionString);
        await connection.OpenAsync(Ct);

        string[] tampering =
        [
            "DELETE FROM album_photos WHERE album_id = @id",
            "DELETE FROM albums WHERE id = @id",
            "UPDATE albums SET created_by_id = 1 WHERE id = @id",
            "UPDATE album_photos SET photo_id = gen_random_uuid() WHERE album_id = @id",
        ];
        foreach (var sql in tampering)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", album.Id);
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
    }

    [Fact]
    public async Task Albums_download_as_a_zip_named_after_them_within_the_limit()
    {
        var (older, newer, _) = await SeedThreeAsync();
        var admin = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.AdminId));
        var album = await CreateAsync(admin, "Beach: 2019?", [older, newer]);
        var created = new List<Guid>();
        try
        {
            var accepted = await admin.PostAsJsonAsync("/api/downloads", new CreateDownloadRequest(null, album.Id), Json, Ct);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            var download = (await accepted.Content.ReadFromJsonAsync<DownloadResponse>(Json, Ct))!;
            created.Add(download.Id);
            Assert.Equal("Beach 2019.zip", download.FileName);
            var photoIds = await factory.WithDbAsync(db => db.Downloads.AsNoTracking().Where(d => d.Id == download.Id).Select(d => d.PhotoIds).SingleAsync(Ct));
            Assert.Equal([older, newer], photoIds);

            var both = await admin.PostAsJsonAsync("/api/downloads", new CreateDownloadRequest([older], album.Id), Json, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);

            var empty = await CreateAsync(admin, "Empty", []);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/downloads", new CreateDownloadRequest(null, empty.Id), Json, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/downloads", new CreateDownloadRequest(null, Guid.NewGuid()), Json, Ct)).StatusCode);

            // One download holds at most 500 photos; a bigger album has to be downloaded in parts, by selection.
            var many = await Seed.PhotosAsync(factory, [.. Enumerable.Range(0, 501).Select(_ => new PhotoSeed())]);
            var big = await CreateAsync(admin, "Big", [.. many.Take(500)]);
            await AddAsync(admin, big.Id, [many[500]]);
            var tooBig = await admin.PostAsJsonAsync("/api/downloads", new CreateDownloadRequest(null, big.Id), Json, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
            Assert.Contains("500", await tooBig.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }
        finally
        {
            // Pending downloads count against the requester's limit of three; later tests start from none.
            await factory.WithDbAsync(db => db.Downloads.Where(d => created.Contains(d.Id)).ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DownloadStatus.Ready), Ct));
        }
    }

    // Two library photos taken four days apart (older first) and a hidden duplicate.
    private async Task<(Guid Older, Guid Newer, Guid Duplicate)> SeedThreeAsync()
    {
        var ids = await Seed.PhotosAsync(
            factory,
            new PhotoSeed(TakenAt: July1.AddDays(4)),
            new PhotoSeed(TakenAt: July1),
            new PhotoSeed(TakenAt: July1.AddDays(-1), Status: PhotoStatus.Duplicate));
        return (ids[1], ids[0], ids[2]);
    }

    private static async Task<AlbumSummary> CreateAsync(HttpClient client, string name, Guid[] photoIds)
    {
        var response = await client.PostAsJsonAsync("/api/albums", new CreateAlbumRequest(name, photoIds), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AlbumSummary>(Json, Ct))!;
    }

    private static async Task<AlbumSummary> AddAsync(HttpClient client, Guid albumId, Guid[] photoIds)
    {
        var response = await client.PostAsJsonAsync($"/api/albums/{albumId}/photos", new AlbumPhotosRequest(photoIds), Json, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AlbumSummary>(Json, Ct))!;
    }

    private static async Task<AlbumSummary> RemoveAsync(HttpClient client, Guid albumId, Guid[] photoIds)
    {
        var response = await client.PostAsJsonAsync($"/api/albums/{albumId}/photos/remove", new AlbumPhotosRequest(photoIds), Json, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AlbumSummary>(Json, Ct))!;
    }
}
