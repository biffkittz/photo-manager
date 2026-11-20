using PhotoManager.Core.Domain;

namespace PhotoManager.Core.Workflows;

/// <summary>Input (and continue-as-new state) of the <see cref="WorkflowTypes.Backfill"/> workflow.</summary>
/// <param name="JobId">Job record tracking this run.</param>
/// <param name="Kind"><see cref="JobKind.BackfillRenditions"/> or <see cref="JobKind.BackfillDescriptions"/>.</param>
/// <param name="AfterPhotoId">Keyset cursor: only photos with a greater id are processed.</param>
/// <param name="Progress">Counters carried across continue-as-new.</param>
public sealed record BackfillInput(
    Guid JobId,
    JobKind Kind,
    Guid? AfterPhotoId = null,
    BackfillProgress? Progress = null);
