using System.Globalization;
using Microsoft.Extensions.Options;
using PhotoManager.Core.Domain;
using PhotoManager.Infrastructure.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageSharpConfiguration = SixLabors.ImageSharp.Configuration;

namespace PhotoManager.Infrastructure.Imaging;

/// <summary>
/// Decoding, validation and resizing with ImageSharp (fully managed: no native codecs in the container,
/// which keeps the attack surface small). All outputs are re-encoded from pixels, so they carry no EXIF,
/// GPS, XMP or IPTC metadata from the original.
/// </summary>
/// <remarks>
/// Hardening, because both uploads and archive content are untrusted input to a decoder:
/// <list type="bullet">
/// <item>Only the JPEG, PNG, GIF and WebP decoders are registered, for identification as well as decoding, so no
/// other ImageSharp decoder (TIFF, BMP, TGA, PBM, QOI...) is ever reachable.</item>
/// <item>Pixels are decoded as 8-bit RGBA whatever the source depth (a 16-bit PNG would otherwise take 8 bytes a
/// pixel), at most <see cref="ImagingOptions.MaxConcurrentDecodes"/> images at a time per process, and ImageSharp's
/// allocations are capped at <see cref="ImagingOptions.DecodeMemoryLimitMegabytes"/>. Overload then fails a
/// decode (which Temporal retries) instead of the container running out of memory and killing every activity.</item>
/// <item>Any decoder failure is reported as <see cref="UnsupportedImageException"/> (permanent, not retried).</item>
/// </list>
/// </remarks>
public sealed class PhotoImageProcessor : IDisposable
{
    private readonly ImagingOptions _options;
    private readonly ImageSharpConfiguration _configuration;
    private readonly SemaphoreSlim _decodeSlots;

    /// <summary>Initializes a new instance of the <see cref="PhotoImageProcessor"/> class.</summary>
    /// <param name="options">Imaging options.</param>
    public PhotoImageProcessor(IOptions<ImagingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _configuration = new ImageSharpConfiguration(
            new JpegConfigurationModule(),
            new PngConfigurationModule(),
            new GifConfigurationModule(),
            new WebpConfigurationModule())
        {
            MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions
            {
                AccumulativeAllocationLimitMegabytes = _options.DecodeMemoryLimitMegabytes,
            }),
        };
        _decodeSlots = new SemaphoreSlim(_options.MaxConcurrentDecodes, _options.MaxConcurrentDecodes);
    }

    /// <summary>Validates an image and extracts its basic facts without decoding pixels.</summary>
    /// <param name="image">Seekable plaintext image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The inspection result.</returns>
    /// <exception cref="UnsupportedImageException">The content is not a supported image or is too large.</exception>
    public async Task<ImageInspection> InspectAsync(Stream image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var info = await IdentifyAsync(image, cancellationToken).ConfigureAwait(false);
        return new ImageInspection(ContentTypeOf(info), info.Width, info.Height, ReadTakenAt(info.Metadata.ExifProfile));
    }

    /// <summary>Creates the WebP thumbnail and preview renditions.</summary>
    /// <param name="image">Seekable plaintext image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The encoded renditions.</returns>
    /// <exception cref="UnsupportedImageException">The content is not a supported, decodable image.</exception>
    public async Task<RenditionSet> CreateRenditionsAsync(Stream image, CancellationToken cancellationToken)
    {
        await _decodeSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var decoded = await LoadOrientedAsync(image, _options.PreviewMaxDimension, cancellationToken).ConfigureAwait(false);
            var encoder = new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = _options.RenditionQuality };

            var preview = await EncodeAsync(decoded, _options.PreviewMaxDimension, encoder, cancellationToken).ConfigureAwait(false);
            var thumbnail = await EncodeAsync(decoded, _options.ThumbnailMaxDimension, encoder, cancellationToken).ConfigureAwait(false);
            return new RenditionSet(thumbnail, preview);
        }
        finally
        {
            _decodeSlots.Release();
        }
    }

    /// <summary>Creates the downscaled, metadata-free JPEG that is sent to Gemini.</summary>
    /// <param name="image">Seekable plaintext image.</param>
    /// <param name="maxDimension">Longest edge in pixels.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JPEG bytes.</returns>
    /// <exception cref="UnsupportedImageException">The content is not a supported, decodable image.</exception>
    public async Task<byte[]> CreateAiInputAsync(Stream image, int maxDimension, CancellationToken cancellationToken)
    {
        await _decodeSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var decoded = await LoadOrientedAsync(image, maxDimension, cancellationToken).ConfigureAwait(false);
            return await EncodeAsync(decoded, maxDimension, new JpegEncoder { Quality = 85 }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _decodeSlots.Release();
        }
    }

    /// <summary>Parses an EXIF 2.31 offset such as <c>+02:00</c> or <c>-06:00</c> (camera-local time zone).</summary>
    /// <param name="value">The raw tag value.</param>
    /// <param name="offset">The parsed offset.</param>
    /// <returns>False for malformed values and for offsets beyond the valid range of ±14:00.</returns>
    public static bool TryParseUtcOffset(string? value, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        var text = value?.Trim('\0', ' ');
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        // Custom TimeSpan formats cannot express a sign, so it is split off first.
        var negative = text[0] == '-';
        var digits = text[0] is '+' or '-' ? text[1..] : text;
        if (!TimeSpan.TryParseExact(digits, @"hh\:mm", CultureInfo.InvariantCulture, out var parsed) || parsed > TimeSpan.FromHours(14))
        {
            return false;
        }

        offset = negative ? -parsed : parsed;
        return true;
    }

    /// <inheritdoc />
    public void Dispose() => _decodeSlots.Dispose();

    // Images are always decoded to a smaller size, so the source is never cloned at full resolution here.
    private static async Task<byte[]> EncodeAsync(Image source, int maxDimension, IImageEncoder encoder, CancellationToken cancellationToken)
    {
        using var resized = source.Width > maxDimension || source.Height > maxDimension
            ? source.Clone(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(maxDimension, maxDimension) }))
            : source.Clone(_ => { });

        using var output = new MemoryStream();
        await resized.SaveAsync(output, encoder, cancellationToken).ConfigureAwait(false);
        return output.ToArray();
    }

    private static DateTimeOffset? ReadTakenAt(ExifProfile? exif)
    {
        if (exif is null || !exif.TryGetValue(ExifTag.DateTimeOriginal, out var taken) || string.IsNullOrWhiteSpace(taken.Value))
        {
            return null;
        }

        if (!DateTime.TryParseExact(taken.Value.Trim('\0', ' '), "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        // EXIF stores camera-local time; OffsetTimeOriginal (EXIF 2.31+) says which zone. Without it the
        // offset is unknown and UTC is assumed, which is good enough for sorting and day-level display.
        if (exif.TryGetValue(ExifTag.OffsetTimeOriginal, out var offset) && TryParseUtcOffset(offset.Value, out var utcOffset))
        {
            try
            {
                // The offset says which instant the camera-local time means; the instant is then stored as UTC
                // (PostgreSQL timestamptz has no offset, and Npgsql refuses non-UTC DateTimeOffset values).
                return new DateTimeOffset(local, utcOffset).ToUniversalTime();
            }
            catch (ArgumentOutOfRangeException)
            {
                // The instant falls outside the representable range (e.g. a bogus 0001-01-01 date); use UTC below.
            }
        }

        return new DateTimeOffset(local, TimeSpan.Zero);
    }

    private static string ContentTypeOf(ImageInfo info)
    {
        var contentType = info.Metadata.DecodedImageFormat?.DefaultMimeType;
        return contentType is not null && ImageSignatures.SupportedContentTypes.Contains(contentType)
            ? contentType
            : throw new UnsupportedImageException("The file is not a supported image type (JPEG, PNG, GIF or WebP).");
    }

    // Decoder failures of any kind mean the content cannot be used: corrupt data, an unsupported variant of a
    // format, or a truncated file. Retrying would never help, so they all become UnsupportedImageException. The input
    // is always an in-memory plaintext buffer, so these cannot come from I/O. InvalidDataException is what a PNG with
    // a valid header but corrupt compressed pixel data produces (from the zlib layer, not as an ImageFormatException).
    private static bool IsDecodeFailure(Exception exception) =>
        exception is ImageFormatException or InvalidDataException or NotSupportedException or EndOfStreamException;

    private async Task<ImageInfo> IdentifyAsync(Stream image, CancellationToken cancellationToken)
    {
        image.Position = 0;
        ImageInfo info;
        try
        {
            info = await Image.IdentifyAsync(Decoder(maxDimension: null), image, cancellationToken).ConfigureAwait(false);
        }
        catch (UnknownImageFormatException ex)
        {
            throw new UnsupportedImageException("The file is not a supported image type (JPEG, PNG, GIF or WebP).", ex);
        }
        catch (Exception ex) when (IsDecodeFailure(ex))
        {
            throw new UnsupportedImageException("The image data is corrupt.", ex);
        }

        if ((long)info.Width * info.Height > _options.MaxPixels)
        {
            throw new UnsupportedImageException(string.Create(CultureInfo.InvariantCulture, $"The image is too large to process ({info.Width}x{info.Height})."));
        }

        return info;
    }

    private async Task<Image> LoadOrientedAsync(Stream image, int targetDimension, CancellationToken cancellationToken)
    {
        // Identify first so the type allowlist and the pixel limit are enforced before any pixel buffer exists.
        ContentTypeOf(await IdentifyAsync(image, cancellationToken).ConfigureAwait(false));
        image.Position = 0;
        Image decoded;
        try
        {
            // TargetSize lets the JPEG decoder scale during decode (DCT scaling), which saves most of the memory and
            // time for large photos; other formats are decoded in full and resized straight away, so everything
            // after this call works on an image no larger than targetDimension.
            decoded = await Image.LoadAsync<Rgba32>(Decoder(targetDimension), image, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsDecodeFailure(ex))
        {
            throw new UnsupportedImageException("The image could not be decoded.", ex);
        }

        try
        {
            decoded.Mutate(x => x.AutoOrient());
        }
        catch
        {
            decoded.Dispose();
            throw;
        }

        decoded.Metadata.ExifProfile = null;
        decoded.Metadata.XmpProfile = null;
        decoded.Metadata.IptcProfile = null;
        decoded.Metadata.IccProfile = null;
        return decoded;
    }

    private DecoderOptions Decoder(int? maxDimension) => new()
    {
        Configuration = _configuration,
        MaxFrames = 1,
        TargetSize = maxDimension is { } size ? new Size(size, size) : null,
    };
}
