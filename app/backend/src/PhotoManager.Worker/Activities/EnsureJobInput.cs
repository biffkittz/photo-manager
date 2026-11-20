using PhotoManager.Core.Domain;

namespace PhotoManager.Worker.Activities;

/// <summary>Input of the EnsureJob activity.</summary>
/// <param name="JobId">Deterministic job id (created if missing, e.g. for scheduled runs).</param>
/// <param name="Kind">Job kind.</param>
/// <param name="WorkflowId">The executing workflow.</param>
/// <param name="RunId">First run of the workflow chain, so a cancel targets this job and not a later one.</param>
public sealed record EnsureJobInput(Guid JobId, JobKind Kind, string WorkflowId, string? RunId = null);
