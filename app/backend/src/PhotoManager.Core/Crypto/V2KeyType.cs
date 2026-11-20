namespace PhotoManager.Core.Crypto;

/// <summary>How the master key of a <see cref="EncryptionFormat.V2"/> file is obtained. Persisted in file headers.</summary>
public enum V2KeyType : byte
{
    /// <summary>Master key = PBKDF2-HMAC-SHA256(password, kdf_salt, kdf_iterations). Used for archive originals.</summary>
    Password = 1,

    /// <summary>Master key is a random 256-bit key (the "web key"). Used for renditions, staging and downloads.</summary>
    RawKey = 2,
}
