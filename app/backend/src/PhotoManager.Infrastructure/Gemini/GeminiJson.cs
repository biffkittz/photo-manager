using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoManager.Infrastructure.Gemini;

// Minimal request/response shapes for models/{model}:generateContent. Only the fields this app uses are
// modelled; unknown response fields are ignored.

/// <summary>generateContent request body.</summary>
internal sealed record GenerateContentRequest(
    IReadOnlyList<GeminiContent> Contents,
    GeminiContent SystemInstruction,
    GeminiGenerationConfig GenerationConfig);

/// <summary>A content turn.</summary>
internal sealed record GeminiContent(IReadOnlyList<GeminiPart> Parts, string? Role = null);

/// <summary>A content part (text or inline data). Thought parts are marked by <see cref="Thought"/>.</summary>
internal sealed record GeminiPart(string? Text = null, GeminiInlineData? InlineData = null, bool? Thought = null);

/// <summary>Inline binary data.</summary>
internal sealed record GeminiInlineData(string MimeType, string Data);

/// <summary>Generation settings, including structured (JSON schema) output.</summary>
internal sealed record GeminiGenerationConfig(
    double Temperature,
    int MaxOutputTokens,
    string ResponseMimeType,
    JsonElement ResponseSchema,
    GeminiThinkingConfig? ThinkingConfig = null);

/// <summary>Reasoning effort for thinking models.</summary>
internal sealed record GeminiThinkingConfig(string ThinkingLevel);

/// <summary>generateContent response body.</summary>
internal sealed record GenerateContentResponse(
    IReadOnlyList<GeminiCandidate>? Candidates,
    GeminiPromptFeedback? PromptFeedback,
    string? ModelVersion);

/// <summary>A response candidate.</summary>
internal sealed record GeminiCandidate(GeminiContent? Content, string? FinishReason);

/// <summary>Prompt-level safety feedback.</summary>
internal sealed record GeminiPromptFeedback(string? BlockReason);

/// <summary>The structured output the prompt asks for.</summary>
internal sealed record GeminiDescriptionResult(string? Description, IReadOnlyList<string>? Tags);

/// <summary>Source-generated serializer context for Gemini payloads.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GenerateContentRequest))]
[JsonSerializable(typeof(GenerateContentResponse))]
[JsonSerializable(typeof(GeminiDescriptionResult))]
internal sealed partial class GeminiJsonContext : JsonSerializerContext;
