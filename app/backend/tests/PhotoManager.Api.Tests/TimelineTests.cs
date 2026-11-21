using System.Net;
using System.Text.Json;
using PhotoManager.Api.Contracts;
using PhotoManager.Api.Tests.Support;
using PhotoManager.Core.Domain;

namespace PhotoManager.Api.Tests;

/// <summary>The gallery's year and month filter, its timeline, and "On this day", all in the viewer's calendar.</summary>
[Collection(ApiCollection.Name)]
public sealed class TimelineTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Timeline_counts_months_in_the_viewers_calendar_and_follows_the_search()
    {
        var tag = $"tl{Guid.NewGuid():N}"[..12];
        var ids = await Seed.PhotosAsync(
            factory,
            new PhotoSeed($"{tag} late on the last day of July", TakenAt: new DateTimeOffset(2019, 7, 31, 23, 30, 0, TimeSpan.Zero)),
            new PhotoSeed($"{tag} mid July", TakenAt: new DateTimeOffset(2019, 7, 10, 12, 0, 0, TimeSpan.Zero)),
            new PhotoSeed($"{tag} no capture date, so the upload date counts", UploadedAt: new DateTimeOffset(2015, 3, 3, 12, 0, 0, TimeSpan.Zero)),
            new PhotoSeed($"{tag} hidden duplicate", TakenAt: new DateTimeOffset(2019, 7, 12, 12, 0, 0, TimeSpan.Zero), Status: PhotoStatus.Duplicate));
        var (lateJuly, midJuly, upload) = (ids[0], ids[1], ids[2]);
        var client = factory.ClientFor(ApiFactory.MemberId);

        var utc = await client.GetFromJsonAsync<List<TimelineMonth>>($"/api/photos/timeline?q={tag}", Json, Ct);
        Assert.Equal([new TimelineMonth(2019, 7, 2), new TimelineMonth(2015, 3, 1)], utc);

        // One hour east of UTC, 23:30 on July 31 is already August.
        var east = await client.GetFromJsonAsync<List<TimelineMonth>>($"/api/photos/timeline?q={tag}&offset=60", Json, Ct);
        Assert.Equal([new TimelineMonth(2019, 8, 1), new TimelineMonth(2019, 7, 1), new TimelineMonth(2015, 3, 1)], east);

        Assert.Equal([lateJuly, midJuly], await IdsAsync(client, $"/api/photos?q={tag}&year=2019&sort=taken"));
        Assert.Equal([lateJuly, midJuly], await IdsAsync(client, $"/api/photos?q={tag}&year=2019&month=7&sort=taken"));
        Assert.Equal([midJuly], await IdsAsync(client, $"/api/photos?q={tag}&year=2019&month=7&offset=60"));
        Assert.Equal([lateJuly], await IdsAsync(client, $"/api/photos?q={tag}&year=2019&month=8&offset=60"));
        Assert.Equal([upload], await IdsAsync(client, $"/api/photos?q={tag}&year=2015"));
        Assert.Empty(await IdsAsync(client, $"/api/photos?q={tag}&year=2016"));

        foreach (var invalid in new[] { "month=7", "year=2019&month=13", "year=0", "year=2019&offset=900", "offset=-841" })
        {
            var response = await client.GetAsync($"/api/photos?{invalid}", Ct);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, invalid);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/photos/timeline?offset=841", Ct)).StatusCode);
    }

    [Fact]
    public async Task On_this_day_shows_earlier_years_on_the_viewers_date()
    {
        var ids = await Seed.PhotosAsync(
            factory,
            new PhotoSeed(TakenAt: new DateTimeOffset(2025, 3, 17, 9, 0, 0, TimeSpan.Zero)),
            new PhotoSeed(TakenAt: new DateTimeOffset(2019, 3, 17, 18, 0, 0, TimeSpan.Zero)),
            new PhotoSeed(TakenAt: new DateTimeOffset(2019, 3, 17, 8, 0, 0, TimeSpan.Zero)),
            new PhotoSeed(TakenAt: new DateTimeOffset(2026, 3, 17, 8, 0, 0, TimeSpan.Zero)), // this year: not a memory
            new PhotoSeed(TakenAt: new DateTimeOffset(2025, 3, 18, 8, 0, 0, TimeSpan.Zero)), // another day
            new PhotoSeed(UploadedAt: new DateTimeOffset(2024, 3, 17, 8, 0, 0, TimeSpan.Zero)), // no capture date
            new PhotoSeed(TakenAt: new DateTimeOffset(2025, 3, 17, 9, 0, 0, TimeSpan.Zero), Status: PhotoStatus.Duplicate),
            new PhotoSeed(TakenAt: new DateTimeOffset(2021, 3, 16, 23, 30, 0, TimeSpan.Zero))); // March 17 an hour east of UTC
        var client = factory.ClientFor(ApiFactory.MemberId);

        var years = Mine(await client.GetFromJsonAsync<List<OnThisDayYear>>("/api/photos/on-this-day?date=2026-03-17", Json, Ct));
        Assert.Equal([(2025, 1), (2019, 7)], years.Select(y => (y.Year, y.YearsAgo)));
        Assert.Equal([ids[0]], years[0].Photos.Select(p => p.Id));
        Assert.Equal([ids[2], ids[1]], years[1].Photos.Select(p => p.Id)); // in the order they were taken

        var east = Mine(await client.GetFromJsonAsync<List<OnThisDayYear>>("/api/photos/on-this-day?date=2026-03-17&offset=60", Json, Ct));
        Assert.Equal([2025, 2021, 2019], east.Select(y => y.Year));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/photos/on-this-day", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/photos/on-this-day?date=2026-02-30", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/photos/on-this-day?date=2026-03-17&offset=900", Ct)).StatusCode);

        // Only this test's photos: other tests share the database.
        List<OnThisDayYear> Mine(List<OnThisDayYear>? all) =>
        [
            .. all!.Select(y => y with { Photos = [.. y.Photos.Where(p => ids.Contains(p.Id))] }).Where(y => y.Photos.Count > 0),
        ];
    }

    private static async Task<Guid[]> IdsAsync(HttpClient client, string url) =>
        [.. (await client.GetFromJsonAsync<PhotoPage>(url, Json, Ct))!.Items.Select(p => p.Id)];
}
