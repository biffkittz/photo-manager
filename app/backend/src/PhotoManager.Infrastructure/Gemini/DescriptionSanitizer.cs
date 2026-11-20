using System.Globalization;
using System.Text;

namespace PhotoManager.Infrastructure.Gemini;

/// <summary>
/// Normalizes model output before it is stored. Model output is untrusted input (an image can contain text
/// that tries to steer the model), so it is reduced to a short single line of plain text. The UI always
/// renders it as text, never as HTML.
/// </summary>
public static class DescriptionSanitizer
{
    /// <summary>Maximum stored description length.</summary>
    public const int MaxDescriptionLength = 200;

    /// <summary>Maximum number of tags kept.</summary>
    public const int MaxTags = 10;

    private const int MaxTagLength = 32;

    /// <summary>Sanitizes a description: one line, no control/format characters, collapsed whitespace, length-capped.</summary>
    /// <param name="value">Raw model output.</param>
    /// <returns>The sanitized description (may be empty).</returns>
    public static string Description(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(value.Length, MaxDescriptionLength));
        var previousSpace = false;
        foreach (var rune in value.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
            {
                if (Rune.IsWhiteSpace(rune) && !previousSpace)
                {
                    builder.Append(' ');
                    previousSpace = true;
                }

                continue;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                if (!previousSpace)
                {
                    builder.Append(' ');
                    previousSpace = true;
                }

                continue;
            }

            previousSpace = false;
            builder.Append(rune);
        }

        var text = builder.ToString().Trim().Trim('"', '\'', '`').TrimEnd('.').Trim();
        if (text.Length <= MaxDescriptionLength)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', MaxDescriptionLength);
        return text[..(cut > MaxDescriptionLength / 2 ? cut : MaxDescriptionLength)].TrimEnd();
    }

    /// <summary>Sanitizes tags: lowercase letters, digits, spaces and hyphens only; deduplicated and capped.</summary>
    /// <param name="tags">Raw model tags.</param>
    /// <returns>Clean tags.</returns>
    public static IReadOnlyList<string> Tags(IEnumerable<string?>? tags)
    {
        if (tags is null)
        {
            return [];
        }

        var result = new List<string>(MaxTags);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var cleaned = new string([.. raw.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-')]).Trim();
            if (cleaned.Length is 0 or > MaxTagLength || !seen.Add(cleaned))
            {
                continue;
            }

            result.Add(cleaned);
            if (result.Count == MaxTags)
            {
                break;
            }
        }

        return result;
    }
}
