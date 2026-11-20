using PhotoManager.Core.Domain;

namespace PhotoManager.Api.Contracts;

/// <summary>Result of staging one upload.</summary>
/// <param name="PhotoId">The new photo id.</param>
/// <param name="Status">Initial status.</param>
public sealed record UploadAccepted(Guid PhotoId, PhotoStatus Status);
