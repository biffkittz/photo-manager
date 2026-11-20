using PhotoManager.Core.Domain;

namespace PhotoManager.Worker.Activities;

/// <summary>Input of the FindBackfillCandidates activity.</summary>
/// <param name="Kind">Backfill kind.</param>
/// <param name="AfterPhotoId">Keyset cursor.</param>
/// <param name="Limit">Batch size.</param>
public sealed record BackfillQuery(JobKind Kind, Guid? AfterPhotoId, int Limit);
