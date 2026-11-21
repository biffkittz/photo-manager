using PhotoManager.Core.Crypto;
using PhotoManager.Core.Domain;
using PhotoManager.Data.Entities;

namespace PhotoManager.Api.Tests.Support;

/// <summary>Seeds photos as the database owner (the API under test runs as the web role).</summary>
internal static class Seed
{
    /// <summary>Adds photos and returns their ids, in the order of <paramref name="photos"/>.</summary>
    public static Task<Guid[]> PhotosAsync(ApiFactory factory, params PhotoSeed[] photos) =>
        factory.WithDbAsync(async db =>
        {
            var rows = photos.Select(seed =>
            {
                var id = Guid.CreateVersion7();
                return new Photo
                {
                    Id = id,
                    Source = PhotoSource.Archive,
                    Status = seed.Status,
                    EncryptionFormat = EncryptionFormat.LegacyV1,
                    ArchiveKey = $"2024-01-01/{id:N}.jpg.encrypted",
                    OriginalFileName = seed.FileName ?? $"{id:N}.jpg",
                    ContentType = "image/jpeg",
                    SizeBytes = 1024,
                    Description = seed.Description,
                    UploadedAt = seed.UploadedAt ?? DateTimeOffset.UtcNow,
                    TakenAt = seed.TakenAt,
                    RenditionState = ProcessingState.Ready,
                    RenditionVersion = 1,
                };
            }).ToList();
            db.Photos.AddRange(rows);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return rows.Select(p => p.Id).ToArray();
        });

    /// <summary>Adds one photo and returns its id.</summary>
    public static async Task<Guid> PhotoAsync(ApiFactory factory, PhotoSeed photo) => (await PhotosAsync(factory, photo))[0];
}

/// <summary>What a seeded photo looks like; library (<see cref="PhotoStatus.Ready"/>) photos by default.</summary>
/// <param name="Description">Description (searchable).</param>
/// <param name="TakenAt">Capture time, if known.</param>
/// <param name="UploadedAt">Upload time (default: now).</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="FileName">Original filename (default: the id).</param>
internal sealed record PhotoSeed(
    string? Description = null,
    DateTimeOffset? TakenAt = null,
    DateTimeOffset? UploadedAt = null,
    PhotoStatus Status = PhotoStatus.Ready,
    string? FileName = null);
