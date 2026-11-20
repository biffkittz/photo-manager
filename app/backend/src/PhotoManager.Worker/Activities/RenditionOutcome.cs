namespace PhotoManager.Worker.Activities;

/// <summary>Result of creating renditions for an archived photo.</summary>
public enum RenditionOutcome
{
    /// <summary>Renditions were created and the photo's facts recorded.</summary>
    Created,

    /// <summary>The photo is a byte-identical copy of another photo in the library and was hidden as a duplicate.</summary>
    Duplicate,
}
