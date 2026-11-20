namespace PhotoManager.Core.Workflows;

/// <summary>Input of single-photo workflows (<see cref="WorkflowTypes.EnsureRenditions"/>, <see cref="WorkflowTypes.DescribePhoto"/>).</summary>
/// <param name="PhotoId">The photo to process.</param>
public sealed record PhotoWorkInput(Guid PhotoId);
