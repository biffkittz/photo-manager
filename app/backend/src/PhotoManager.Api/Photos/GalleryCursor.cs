using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace PhotoManager.Api.Photos;

/// <summary>Opaque keyset-pagination cursor: the sort timestamp and id of the last item on a page.</summary>
/// <param name="Time">Sort timestamp of the last item.</param>
/// <param name="Id">Id of the last item (tie-breaker).</param>
public sealed record GalleryCursor(DateTimeOffset Time, Guid Id)
{
    /// <summary>Encodes the cursor as base64url.</summary>
    /// <returns>The opaque cursor string.</returns>
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{Time.UtcTicks}:{Id:N}")));

    /// <summary>Parses a cursor produced by <see cref="Encode"/>; anything else is rejected.</summary>
    /// <param name="value">The cursor string.</param>
    /// <param name="cursor">The parsed cursor.</param>
    /// <returns>Whether parsing succeeded.</returns>
    public static bool TryParse(string? value, out GalleryCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(value) || value.Length > 100)
        {
            return false;
        }

        try
        {
            var parts = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(value)).Split(':');
            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks is > 0 and < 3155378975999999999
                && Guid.TryParseExact(parts[1], "N", out var id))
            {
                cursor = new GalleryCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }
}
