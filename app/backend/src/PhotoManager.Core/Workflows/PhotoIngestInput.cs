namespace PhotoManager.Core.Workflows;

/// <summary>Input of the <see cref="WorkflowTypes.PhotoIngest"/> workflow. Only identifiers cross Temporal; data stays in storage.</summary>
/// <param name="PhotoId">The photo whose staged upload should be ingested.</param>
/// <param name="SkipDescription">True when the uploader switched AI descriptions off: the photo is archived without
/// waiting for Gemini and can be described later. Defaults to false, so inputs recorded before the option existed
/// keep describing.</param>
public sealed record PhotoIngestInput(Guid PhotoId, bool SkipDescription = false);
