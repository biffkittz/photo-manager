using System.Globalization;
using Microsoft.Extensions.Options;
using PhotoManager.Core.Domain;
using PhotoManager.Infrastructure.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

namespace PhotoManager.Infrastructure.Imaging;

/// <summary>
/// Decoding, validation and resizing with ImageSharp (fully managed: no native codecs in the container,
/// which keeps the attack surface small). All outputs are re-encoded from pixels, so they carry no EXIF,
/// GPS, XMP or IPTC metadata from the original.
/// </summary>
/// <param name="options">Imaging options.</param>
public sealed class PhotoImageProcessor(IOptions<ImagingOptions> options)
{
    private readonly ImagingOptions _options = options.Value;

    /// <summary>Validates an image and extracts its basic facts without decoding pixels.</summary>
    /// <param name="image">Seekable plaintext image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The inspection result.</returns>
    /// <exception cref="UnsupportedImageException">The content is not a supported image or is too large.</exception>
    public async Task<ImageInspection> InspectAsync(Stream image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var info = await IdentifyAsync(image, cancellationToken).ConfigureAwait(false);
        var contentType = info.Metadata.DecodedImageFormat?.DefaultMimeType;
        if (contentType is null || !ImageSignatures.SupportedContentTypes.Contains(contentType))
        {
            throw new UnsupportedImageException("The file is not a supported image type (JPEG, PNG, GIF or WebP).");
        }

        return new ImageInspection(contentType, info.Width, info.Height, ReadTakenAt(info.Metadata.ExifProfile));
    }

    /// <summary>Creates the WebP thumbnail and preview renditions.</summary>
    /// <param name="image">Seekable plaintext image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The encoded renditions.</returns>
    public async Task<RenditionSet> CreateRenditionsAsync(Stream image, CancellationToken cancellationToken)
    {
        using var decoded = await LoadOrientedAsync(image, _options.PreviewMaxDimension, cancellationToken).ConfigureAwait(false);
        var encoder = new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = _options.RenditionQuality };

        var preview = await EncodeAsync(decoded, _options.PreviewMaxDimension, encoder, cancellationToken).ConfigureAwait(false);
        var thumbnail = await EncodeAsync(decoded, _options.ThumbnailMaxDimension, encoder, cancellationToken).ConfigureAwait(false);
        return new RenditionSet(thumbnail, preview);
    }

    /// <summary>Creates the downscaled, metadata-free JPEG that is sent to Gemini.</summary>
    /// <param name="image">Seekable plaintext image.</param>
    /// <param name="maxDimension">Longest edge in pixels.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JPEG bytes.</returns>
    public async Task<byte[]> CreateAiInputAsync(Stream image, int maxDimension, CancellationToken cancellationToken)
    {
        using var decoded = await LoadOrientedAsync(image, maxDimension, cancellationToken).ConfigureAwait(false);
        return await EncodeAsync(decoded, maxDimension, new JpegEncoder { Quality = 85 }, cancellationToken).ConfigureAwait(false);
    }

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
                return new DateTimeOffset(local, utcOffset);
            }
            catch (ArgumentOutOfRangeException)
            {
                // The instant falls outside the representable range (e.g. a bogus 0001-01-01 date); use UTC below.
            }
        }

        return new DateTimeOffset(local, TimeSpan.Zero);
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

    private async Task<ImageInfo> IdentifyAsync(Stream image, CancellationToken cancellationToken)
    {
        image.Position = 0;
        ImageInfo info;
        try
        {
            info = await Image.IdentifyAsync(new DecoderOptions { MaxFrames = 1 }, image, cancellationToken).ConfigureAwait(false);
        }
        catch (UnknownImageFormatException ex)
        {
            throw new UnsupportedImageException("The file is not a supported image.", ex);
        }
        catch (InvalidImageContentException ex)
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
        // Identify first so the pixel limit is enforced before any pixel buffer is allocated.
        await IdentifyAsync(image, cancellationToken).ConfigureAwait(false);
        image.Position = 0;
        Image decoded;
        try
        {
            // TargetSize lets the JPEG decoder scale during decode (DCT scaling), which saves most of the
            // memory and time for large photos; other formats are resized after decoding.
            decoded = await Image.LoadAsync(
                new DecoderOptions { MaxFrames = 1, TargetSize = new Size(targetDimension, targetDimension) },
                image,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new UnsupportedImageException("The image could not be decoded.", ex);
        }

        decoded.Mutate(x => x.AutoOrient());
        decoded.Metadata.ExifProfile = null;
        decoded.Metadata.XmpProfile = null;
        decoded.Metadata.IptcProfile = null;
        decoded.Metadata.IccProfile = null;
        return decoded;
    }
}
