namespace PhotoManager.Api.Contracts;

/// <summary>Edits a photo's description.</summary>
/// <param name="Description">New description (at most 300 characters; empty clears it).</param>
public sealed record UpdatePhotoRequest(string? Description);
