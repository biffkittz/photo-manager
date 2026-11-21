using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace PhotoManager.Core.Domain;

/// <summary>
/// Sanitizes user-supplied filenames before they are stored, logged, or echoed in headers, and makes names from
/// photo descriptions.
/// </summary>
public static class FileNames
{
    /// <summary>Maximum stored filename length.</summary>
    public const int MaxLength = 200;

    /// <summary>Maximum length of a name made by <see cref="FromDescription"/>, not counting the extension.</summary>
    public const int MaxDescriptiveStemLength = 60;

    private const string Fallback = "photo";

    // Words that read as unfinished at the end of a shortened name ("kids building a sandcastle on a").
    private static readonly FrozenSet<string> TrailingFillers = FrozenSet.Create(
        StringComparer.Ordinal,
        ["a", "an", "and", "around", "as", "at", "behind", "beside", "between", "by", "for", "from", "her", "his", "in",
         "into", "its", "near", "of", "on", "onto", "or", "over", "some", "the", "their", "to", "under", "while", "with"]);

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
            if (Rune.IsControl(rune) || IsBidiControl(rune) || IsInvalidFileNameChar(rune))
            {
                continue;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                if (!previousWasSpace && builder.Length > 0)
                {
                    if (builder.Length >= MaxLength)
                    {
                        break;
                    }

                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            if (builder.Length + rune.Utf16SequenceLength > MaxLength)
            {
                break;
            }

            previousWasSpace = false;
            builder.Append(rune);
        }

        var result = builder.ToString().Trim(' ', '.');
        return result.Length == 0 ? Fallback : result;
    }

    /// <summary>Replaces the extension of <paramref name="fileName"/> with <paramref name="extension"/>.</summary>
    /// <param name="fileName">A sanitized filename.</param>
    /// <param name="extension">The extension including the dot.</param>
    /// <returns>The adjusted filename, guaranteed to fit within <see cref="MaxLength"/>.</returns>
    public static string WithExtension(string fileName, string extension)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(extension.Length, MaxLength);

        var current = Path.GetExtension(fileName);
        var keepCurrent = string.Equals(current, extension, StringComparison.OrdinalIgnoreCase)
            || (extension == ".jpg" && string.Equals(current, ".jpeg", StringComparison.OrdinalIgnoreCase));
        if (keepCurrent && fileName.Length <= MaxLength)
        {
            return fileName;
        }

        var targetExtension = keepCurrent ? current : extension;
        var maxStemLength = MaxLength - targetExtension.Length;
        var stem = TruncateUtf16Safe(Path.GetFileNameWithoutExtension(fileName), maxStemLength).Trim(' ', '.');
        if (stem.Length == 0)
        {
            stem = TruncateUtf16Safe(Fallback, maxStemLength);
        }

        return stem + targetExtension;
    }

    /// <summary>
    /// Names a photo after its description (docs/adr/0014): the description's words in lowercase joined by
    /// underscores, shortened at a word to at most <see cref="MaxDescriptiveStemLength"/> characters, plus the
    /// lowercased extension of <paramref name="currentFileName"/>; for example "dog_running_in_the_snow.jpg". Only
    /// letters and digits are kept (apostrophes are dropped, anything else separates words), so the name is safe on
    /// common filesystems whatever the description, which is untrusted model output, contains.
    /// </summary>
    /// <param name="description">The description.</param>
    /// <param name="currentFileName">The photo's current (sanitized) file name, which supplies the extension.</param>
    /// <returns>The name, or <see langword="null"/> when the description has no letters or digits.</returns>
    public static string? FromDescription(string? description, string? currentFileName)
    {
        var words = Words(description);
        if (words.Count == 0)
        {
            return null;
        }

        var kept = new List<string>(words.Count);
        var length = -1;
        foreach (var word in words)
        {
            if (length + 1 + word.Length > MaxDescriptiveStemLength)
            {
                break;
            }

            kept.Add(word);
            length += 1 + word.Length;
        }

        if (kept.Count == 0)
        {
            kept.Add(TruncateUtf16Safe(words[0], MaxDescriptiveStemLength));
        }
        else if (kept.Count < words.Count)
        {
            while (kept.Count > 1 && TrailingFillers.Contains(kept[^1]))
            {
                kept.RemoveAt(kept.Count - 1);
            }
        }

        var stem = string.Join('_', kept);

        // Windows refuses device names as file names, whatever the extension ("con.jpg").
        if (stem is "con" or "prn" or "aux" or "nul"
            || (stem.Length == 4 && (stem.StartsWith("com", StringComparison.Ordinal) || stem.StartsWith("lpt", StringComparison.Ordinal)) && char.IsAsciiDigit(stem[3])))
        {
            stem += "_photo";
        }

        return stem + PlainExtension(currentFileName);
    }

    private static List<string> Words(string? text)
    {
        var words = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return words;
        }

        var word = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            // Combining marks belong to the letter before them (a decomposed "é").
            if (Rune.IsLetterOrDigit(rune) || (word.Length > 0 && Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
            {
                word.Append(rune);
                continue;
            }

            // "children's party" becomes "childrens_party": apostrophes join, anything else separates.
            if (rune.Value is '\'' or '\u2019')
            {
                continue;
            }

            if (word.Length > 0)
            {
                words.Add(word.ToString().ToLowerInvariant());
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString().ToLowerInvariant());
        }

        return words;
    }

    // The extension of a sanitized name, lowercased, if it is a plain one (".jpg", ".heic"); otherwise none.
    private static string PlainExtension(string? fileName)
    {
        var extension = Path.GetExtension(fileName) ?? string.Empty;
        return extension.Length is >= 2 and <= 11 && extension[1..].All(char.IsAsciiLetterOrDigit)
            ? extension.ToLowerInvariant()
            : string.Empty;
    }

    private static string TruncateUtf16Safe(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var length = maxLength;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }

    private static bool IsInvalidFileNameChar(Rune rune) =>
        rune.Value is '"' or '*' or ':' or '<' or '>' or '?' or '|' or '/' or '\\';

    // Bidirectional overrides can disguise extensions ("photo‮gpj.exe" renders as "photoexe.jpg").
    private static bool IsBidiControl(Rune rune) =>
        rune.Value is (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069) or 0x200E or 0x200F or 0x061C;
}
