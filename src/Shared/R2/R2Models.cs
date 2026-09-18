namespace Shared;

/// <summary>Metadata of the single object stored in the R2 sample bucket.</summary>
public sealed record R2FileInfo(
    bool Exists,
    string Key,
    string Bucket,
    ulong Size,
    int MaxBytes,
    string ContentType,
    string UploadedAtUtc,
    string Etag);

/// <summary>Result of uploading or deleting the R2 object, with the object as it is now.</summary>
public sealed record R2MutationResult(bool Ok, string Message, R2FileInfo File);
