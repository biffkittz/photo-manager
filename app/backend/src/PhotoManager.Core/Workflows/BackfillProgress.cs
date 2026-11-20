namespace PhotoManager.Core.Workflows;

/// <summary>Counters accumulated by a backfill run.</summary>
/// <param name="Processed">Photos attempted.</param>
/// <param name="Succeeded">Photos completed successfully.</param>
/// <param name="Failed">Photos that failed (left for a later retry).</param>
/// <param name="Duplicates">Photos found to be byte-identical copies of another photo (hidden from the library).</param>
public sealed record BackfillProgress(int Processed = 0, int Succeeded = 0, int Failed = 0, int Duplicates = 0);
