using System.Text;

namespace PhotoManager.Core.Domain;

/// <summary>Sanitizes user-supplied filenames before they are stored, logged, or echoed in headers.</summary>
public static class FileNames
{
    /// <summary>Maximum stored filename length.</summary>
    public const int MaxLength = 200;

    private const string Fallback = "photo";

    /// <summary>
    /// Reduces a client-supplied name to a safe display filename: strips any path (both separators, since
    /// browsers on Windows may send full paths), control and bidi-override characters, and characters that
    /// are invalid on common filesystems; collapses whitespace and limits the length.
    /// </summary>
    /// <param name="name">The untrusted filename.</param>
    /// <returns>A non-empty, safe filename.</returns>
    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fallback;
        }

        var lastSeparator = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        var candidate = lastSeparator >= 0 ? name[(lastSeparator + 1)..] : name;

        var builder = new StringBuilder(Math.Min(candidate.Length, MaxLength));
        var previousWasSpace = false;
        foreach (var rune in candidate.EnumerateRunes())
        {
            if (builder.Length >= MaxLength)
            {
                break;
            }

            if (Rune.IsControl(rune) || IsBidiControl(rune) || IsInvalidFileNameChar(rune))
            {
                continue;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                if (!previousWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            previousWasSpace = false;
            builder.Append(rune);
        }

        var result = builder.ToString().Trim().Trim('.');
        return result.Length == 0 ? Fallback : result;
    }

    /// <summary>Replaces the extension of <paramref name="fileName"/> with <paramref name="extension"/>.</summary>
    /// <param name="fileName">A sanitized filename.</param>
    /// <param name="extension">The extension including the dot.</param>
    /// <returns>The adjusted filename.</returns>
    public static string WithExtension(string fileName, string extension)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var current = Path.GetExtension(fileName);
        if (string.Equals(current, extension, StringComparison.OrdinalIgnoreCase)
            || (extension == ".jpg" && string.Equals(current, ".jpeg", StringComparison.OrdinalIgnoreCase)))
        {
            return fileName;
        }

        return Path.GetFileNameWithoutExtension(fileName) + extension;
    }

    private static bool IsInvalidFileNameChar(Rune rune) =>
        rune.Value is '"' or '*' or ':' or '<' or '>' or '?' or '|' or '/' or '\\';

    // Bidirectional overrides can disguise extensions ("photo‮gpj.exe" renders as "photoexe.jpg").
    private static bool IsBidiControl(Rune rune) =>
        rune.Value is (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069) or 0x200E or 0x200F or 0x061C;
}
