namespace PhotoManager.Infrastructure.Configuration;

/// <summary>Gemini image-description settings. Section <c>Gemini</c>. Worker tier only.</summary>
public sealed class GeminiOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Gemini";

    /// <summary>Gets or sets the API key (Key Vault secret <c>Gemini--ApiKey</c>). Sent in a header, never in the URL.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the API base URL. Gemini Developer API by default; Vertex AI express mode uses
    /// <c>https://aiplatform.googleapis.com/v1/publishers/google</c> with the same request shape.
    /// </summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>
    /// Gets or sets the model id. Pinned (not a "-latest" alias) so behaviour and cost only change deliberately;
    /// verified available on 2026-11-19 and recommended by Google for new projects.
    /// </summary>
    public string Model { get; set; } = "gemini-3.8-flash";

    /// <summary>
    /// Gets or sets the Gemini 3 thinking level ("minimal", "low", "medium", "high"), or null to omit it (required
    /// for models without thinking support). A one-line caption needs little reasoning; low keeps latency and
    /// cost down.
    /// </summary>
    public string? ThinkingLevel { get; set; } = "low";

    /// <summary>
    /// Gets or sets the per-request timeout; keep it below the describe activity's StartToClose (3 minutes).
    /// Generous because a smoke test on 2026-11-19 measured ~83 s for one image with thinking level "low".
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(150);

    /// <summary>Gets or sets the longest edge of the downscaled JPEG sent to the model.</summary>
    public int MaxImageDimension { get; set; } = 1536;

    /// <summary>Gets or sets a value indicating whether to use a deterministic local stub instead of the real API (development only).</summary>
    public bool UseFake { get; set; }
}
