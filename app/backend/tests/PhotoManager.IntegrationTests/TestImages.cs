using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace PhotoManager.IntegrationTests;

/// <summary>Synthetic test photos (never real ones).</summary>
internal static class TestImages
{
    /// <summary>Creates a unique JPEG (random noise keeps hashes unique across runs) with an EXIF capture date.</summary>
    public static byte[] Jpeg(int width = 1600, int height = 1200, string? offsetTimeOriginal = null)
    {
        using var image = new Image<Rgb24>(width, height);
        var random = new Random(Guid.NewGuid().GetHashCode());
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgb24((byte)(x * 255 / width), (byte)(y * 255 / height), (byte)random.Next(256));
                }
            }
        });

        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.DateTimeOriginal, "2024:07:04 18:22:01");
        if (offsetTimeOriginal is not null)
        {
            // What phones record: the camera's time zone, which makes the capture time a non-UTC DateTimeOffset.
            image.Metadata.ExifProfile.SetValue(ExifTag.OffsetTimeOriginal, offsetTimeOriginal);
        }

        using var output = new MemoryStream();
        image.SaveAsJpeg(output);
        return output.ToArray();
    }
}
