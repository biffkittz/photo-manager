namespace PhotoManager.Api.Auth;

/// <summary>Open-redirect protection for post-login return URLs.</summary>
public static class ReturnUrl
{
    /// <summary>
    /// Returns <paramref name="candidate"/> only if it is a local, rooted path ("/uploads"), otherwise "/".
    /// Rejects absolute URLs, protocol-relative URLs ("//evil.example") and backslash tricks ("/\evil").
    /// </summary>
    /// <param name="candidate">The requested return URL.</param>
    /// <returns>A safe local URL.</returns>
    public static string Sanitize(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate) || candidate.Length > 2048 || candidate[0] != '/')
        {
            return "/";
        }

        if (candidate.Length > 1 && (candidate[1] == '/' || candidate[1] == '\\'))
        {
            return "/";
        }

        return candidate.Any(char.IsControl) ? "/" : candidate;
    }
}
