using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PhotoManager.Api.Contracts;
using PhotoManager.Api.Tests.Support;

namespace PhotoManager.Api.Tests;

/// <summary>
/// Photos named after their AI description (docs/adr/0014): the library shows and downloads the new name, the name
/// the photo arrived with is kept and still finds it, and only workers rename photos.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PhotoNameTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_renamed_photo_shows_its_new_name_and_is_found_by_either_name()
    {
        var tag = $"pn{Guid.NewGuid():N}"[..12];
        var id = await Seed.PhotoAsync(factory, new PhotoSeed("dog running in the snow", FileName: $"dog_running_in_the_snow_{tag}.jpg", OriginalFileName: $"IMG_{tag}.JPG"));
        var client = factory.ClientFor(ApiFactory.MemberId);

        var details = await client.GetFromJsonAsync<PhotoDetails>($"/api/photos/{id}", Json, Ct);
        Assert.Equal($"dog_running_in_the_snow_{tag}.jpg", details!.FileName);
        Assert.Equal($"IMG_{tag}.JPG", details.OriginalFileName);

        // The gallery tile carries the new name, whichever name was searched for: the old one, part of the new one
        // (not a whole word, so only the file name matches), or a word of either.
        foreach (var q in new[] { $"IMG_{tag}", $"img_{tag}.jpg", $"ing_in_the_snow_{tag}", tag })
        {
            var page = await client.GetFromJsonAsync<PhotoPage>($"/api/photos?q={Uri.EscapeDataString(q)}", Json, Ct);
            var tile = Assert.Single(page!.Items);
            Assert.Equal(id, tile.Id);
            Assert.Equal($"dog_running_in_the_snow_{tag}.jpg", tile.FileName);
        }
    }

    [Fact]
    public async Task Descriptions_written_by_people_do_not_rename_a_photo()
    {
        var id = await Seed.PhotoAsync(factory, new PhotoSeed("dog running in the snow", FileName: "dog_running_in_the_snow.jpg", OriginalFileName: "IMG_0001.jpg"));
        var client = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.MemberId));

        var edited = await client.PatchAsJsonAsync($"/api/photos/{id}", new UpdatePhotoRequest("Rex in grandma's garden"), Json, Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var details = await edited.Content.ReadFromJsonAsync<PhotoDetails>(Json, Ct);
        Assert.Equal("Rex in grandma's garden", details!.Description);
        Assert.Equal("dog_running_in_the_snow.jpg", details.FileName);
        Assert.Equal("IMG_0001.jpg", details.OriginalFileName);
    }

    [Fact]
    public async Task Photos_the_previous_release_inserts_get_the_name_they_arrived_with()
    {
        // During a rollout, and after a rollback, the previous release runs against this schema. Its inserts (here an
        // upload, as its web tier records one) leave the name column out.
        var id = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(factory.WebConnectionString);
        await connection.OpenAsync(Ct);
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO photos (id, source, status, original_file_name, content_type, description_state, rendition_state,
                                rendition_version, search_keywords, tags, uploaded_at, created_at, updated_at)
            VALUES (@id, 'Upload', 'Queued', 'IMG_0042.jpg', 'image/jpeg', 'Pending', 'Pending', 0, '', '{}', now(), now(), now())
            """,
            connection))
        {
            insert.Parameters.AddWithValue("id", id);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync(Ct));
        }

        var name = await factory.WithDbAsync(db => db.Photos.Where(p => p.Id == id).Select(p => p.FileName).SingleAsync(Ct));
        Assert.Equal("IMG_0042.jpg", name);
    }

    [Fact]
    public async Task Web_database_role_cannot_rename_photos()
    {
        var id = await Seed.PhotoAsync(factory, new PhotoSeed(FileName: "named-by-a-worker.jpg"));
        await using var connection = new NpgsqlConnection(factory.WebConnectionString);
        await connection.OpenAsync(Ct);

        foreach (var sql in new[] { "UPDATE photos SET file_name = 'x.jpg' WHERE id = @id", "UPDATE photos SET original_file_name = 'x.jpg' WHERE id = @id" })
        {
            await using var rename = new NpgsqlCommand(sql, connection);
            rename.Parameters.AddWithValue("id", id);
            var error = await Assert.ThrowsAsync<PostgresException>(() => rename.ExecuteNonQueryAsync(Ct));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
    }
}
