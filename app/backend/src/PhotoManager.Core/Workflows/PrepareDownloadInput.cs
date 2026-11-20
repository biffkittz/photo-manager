namespace PhotoManager.Core.Workflows;

/// <summary>Input of the <see cref="WorkflowTypes.PrepareDownload"/> workflow.</summary>
/// <param name="DownloadId">The prepared-download record to fulfil.</param>
public sealed record PrepareDownloadInput(Guid DownloadId);
