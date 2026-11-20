namespace PhotoManager.Core.Workflows;

/// <summary>Input of the <see cref="WorkflowTypes.PhotoIngest"/> workflow. Only identifiers cross Temporal; data stays in storage.</summary>
/// <param name="PhotoId">The photo whose staged upload should be ingested.</param>
public sealed record PhotoIngestInput(Guid PhotoId);
