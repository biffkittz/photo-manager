namespace PhotoManager.Infrastructure.Storage;

/// <summary>A requested blob or archive object does not exist.</summary>
public sealed class StorageObjectNotFoundException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="StorageObjectNotFoundException"/> class.</summary>
    public StorageObjectNotFoundException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="StorageObjectNotFoundException"/> class.</summary>
    /// <param name="message">Log-safe message (object names contain only identifiers).</param>
    public StorageObjectNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="StorageObjectNotFoundException"/> class.</summary>
    /// <param name="message">Log-safe message.</param>
    /// <param name="innerException">The provider exception.</param>
    public StorageObjectNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
