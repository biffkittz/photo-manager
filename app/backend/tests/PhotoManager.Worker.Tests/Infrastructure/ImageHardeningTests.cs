using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;
using Microsoft.Extensions.Options;
using PhotoManager.Infrastructure.Configuration;
using PhotoManager.Infrastructure.Imaging;
using PhotoManager.Worker.Activities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace PhotoManager.Worker.Tests.Infrastructure;

/// <summary>Decoder hardening from the 2026-11-20 review: format allowlist, failure mapping and pixel depth.</summary>
public sealed class ImageHardeningTests : IDisposable
{
    private readonly PhotoImageProcessor _processor = new(Options.Create(new ImagingOptions()));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _processor.Dispose();

    [Theory]
    [InlineData("bmp")]
    [InlineData("tiff")]
    [InlineData("tga")]
    public async Task Formats_outside_the_allowlist_are_never_decoded(string format)
    {
        using var image = Noise<Rgba32>(16, 16);
        using var file = new MemoryStream();
        switch (format)
        {
            case "bmp":
                await image.SaveAsBmpAsync(file, Ct);
                break;
            case "tiff":
                await image.SaveAsTiffAsync(file, Ct);
                break;
            default:
                await image.SaveAsTgaAsync(file, Ct);
                break;
        }

        await Assert.ThrowsAsync<UnsupportedImageException>(() => _processor.InspectAsync(file, Ct));
        await Assert.ThrowsAsync<UnsupportedImageException>(() => _processor.CreateRenditionsAsync(file, Ct));
        await Assert.ThrowsAsync<UnsupportedImageException>(() => _processor.CreateAiInputAsync(file, 512, Ct));
    }

    [Fact]
    public async Task Image_whose_header_is_valid_but_pixel_data_is_corrupt_is_unsupported_not_retried()
    {
        // Passes inspection (which only reads the header), then fails the full decode with an exception from the
        // zlib layer. It must surface as UnsupportedImage, which ingest treats as "reject the upload".
        using var file = new MemoryStream(PngWithCorruptPixelData());

        var inspection = await _processor.InspectAsync(file, Ct);
        Assert.Equal("image/png", inspection.ContentType);

        await Assert.ThrowsAsync<UnsupportedImageException>(() => _processor.CreateRenditionsAsync(file, Ct));
        Assert.True(ActivityErrors.TryTranslate(new UnsupportedImageException("x"), out var failure));
        Assert.Equal(ActivityErrors.UnsupportedImage, Assert.IsType<Temporalio.Exceptions.ApplicationFailureException>(failure).ErrorType);
    }

    [Fact]
    public async Task Sixteen_bit_images_are_rendered()
    {
        using var image = Noise<Rgba64>(40, 30);
        using var file = new MemoryStream();
        await image.SaveAsPngAsync(file, new PngEncoder { BitDepth = PngBitDepth.Bit16, ColorType = PngColorType.RgbWithAlpha }, Ct);

        var renditions = await _processor.CreateRenditionsAsync(file, Ct);

        Assert.NotEmpty(renditions.Thumbnail);
        Assert.NotEmpty(renditions.Preview);
    }

    [Fact]
    public async Task Decodes_beyond_the_concurrency_limit_wait_instead_of_failing()
    {
        using var processor = new PhotoImageProcessor(Options.Create(new ImagingOptions { MaxConcurrentDecodes = 1 }));
        var files = new byte[4][];
        for (var i = 0; i < files.Length; i++)
        {
            using var image = Noise<Rgba32>(64, 64);
            using var buffer = new MemoryStream();
            await image.SaveAsPngAsync(buffer, Ct);
            files[i] = buffer.ToArray();
        }

        var results = await Task.WhenAll(files.Select(bytes => Task.Run(() => processor.CreateRenditionsAsync(new MemoryStream(bytes), Ct), Ct)));

        Assert.All(results, r => Assert.NotEmpty(r.Thumbnail));
    }

    private static Image<TPixel> Noise<TPixel>(int width, int height)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var random = new Random(42);
        var image = new Image<TPixel>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = TPixel.FromRgba32(new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255));
            }
        }

        return image;
    }

    // A valid PNG whose compressed pixel data is scrambled, with the chunk checksum recomputed so the file still
    // looks intact to anything that does not decompress it.
    private static byte[] PngWithCorruptPixelData()
    {
        using var image = Noise<Rgba32>(64, 64);
        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        var png = buffer.ToArray();

        var type = png.AsSpan().IndexOf("IDAT"u8);
        var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(type - 4, 4));
        for (var i = type + 4 + 6; i < type + 4 + 40; i++)
        {
            png[i] ^= 0x5A;
        }

        var crc = Crc32.HashToUInt32(png.AsSpan(type, 4 + length));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(type + 4 + length, 4), crc);
        Assert.Equal("IDAT", Encoding.ASCII.GetString(png, type, 4));
        return png;
    }
}
