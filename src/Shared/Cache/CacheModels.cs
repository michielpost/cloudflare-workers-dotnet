namespace Shared;

/// <summary>
/// Payload of the Cache API sample. The values below are frozen for as long as
/// the entry stays in the cache, which is what makes a cache hit visible in the UI.
/// </summary>
public sealed record CachePayload(string CacheKey, string EntryId, string GeneratedAtUtc, string Note);

/// <summary>Result of deleting the Cache API entry, so the next request is a miss again.</summary>
public sealed record CachePurgeResult(bool Purged, string CacheKey, string Message);
