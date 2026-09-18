namespace Shared;

public sealed record ApiResponse(bool Ok, string Message, string Path, DateTimeOffset Timestamp);

public sealed record ReadingInput(string DeviceId, double Value, IReadOnlyList<string> Tags);

/// <summary>
/// Body the worker returns for a rejected request (see <c>Results.Error</c> in
/// the worker), so the frontend can show the reason instead of raw text.
/// </summary>
public sealed record ErrorPayload(string Error, string RequestId);

/// <summary>
/// Payload of the Cache API sample. The values below are frozen for as long as
/// the entry stays in the cache, which is what makes a cache hit visible in the UI.
/// </summary>
public sealed record CachePayload(string CacheKey, string EntryId, string GeneratedAtUtc, string Note);

/// <summary>Result of deleting the Cache API entry, so the next request is a miss again.</summary>
public sealed record CachePurgeResult(bool Purged, string CacheKey, string Message);

/// <summary>Request body of the KV sample: one of the fixed keys and its new value.</summary>
public sealed record KvWriteRequest(string Key, string Value);

/// <summary>A single KV entry. <see cref="HasValue"/> is false for keys never written.</summary>
public sealed record KvItem(string Key, string Value, bool HasValue);

public sealed record KvSnapshot(IReadOnlyList<KvItem> Items, int MaxValueLength, string NamespaceName);

public sealed record KvWriteResult(bool Ok, string Message, KvSnapshot Snapshot);

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

public sealed record R2MutationResult(bool Ok, string Message, R2FileInfo File);

/// <summary>Message body put on the queue by the producer sample.</summary>
public sealed record QueueJob(string Text, string QueuedAtUtc);

/// <summary>Request body of the queue producer sample.</summary>
public sealed record QueueMessageInput(string Text);

public sealed record QueueSendResult(bool Ok, string Message, string Text, string QueuedAtUtc);

/// <summary>
/// One processed queue message: the text, when it was put on the queue and when
/// the consumer processed it.
/// </summary>
public sealed record QueueRecord(string Text, string QueuedAtUtc, string ProcessedAtUtc, string MessageId);

public sealed record QueueStatus(
    QueueRecord? Latest,
    IReadOnlyList<QueueRecord> Items,
    string QueueName,
    string NamespaceName,
    string ResultKey,
    int MaxTextLength);

/// <summary>Newest-first list of processed queue messages, stored as one KV value.</summary>
public sealed record QueueHistory(IReadOnlyList<QueueRecord> Items);

/// <summary>Newest-first list of past scheduled runs, stored as one KV value.</summary>
public sealed record ScheduledHistory(IReadOnlyList<ScheduledRun> Items);

/// <summary>Result of a single scheduled (cron) run.</summary>
public sealed record ScheduledRun(
    int RunNumber,
    string Cron,
    string ScheduledForUtc,
    string RanAtUtc,
    bool Manual);

public sealed record ScheduledStatus(ScheduledRun? Latest, IReadOnlyList<ScheduledRun> Items, string Cron, string ResultKey);

/// <summary>
/// Names and limits shared by the worker and the Blazor frontend. The worker
/// also returns the names and limits in its API payloads, so the frontend never
/// has to guess them.
/// </summary>
public static class SampleConfig
{
    /// <summary>KV namespace the queue consumer, the scheduled task and the KV sample write to.</summary>
    public const string KvNamespaceName = "dotnet_test";

    /// <summary>R2 bucket name from wrangler.toml.</summary>
    public const string R2BucketName = "dotnettest";

    /// <summary>Every upload is stored under this key, so uploads overwrite each other.</summary>
    public const string R2ObjectKey = "sample_file.txt";

    /// <summary>Queue the producer sample sends to and the consumer sample reads from.</summary>
    public const string QueueName = "dotnet-queue";

    /// <summary>KV key the queue consumer always writes its latest result to.</summary>
    public const string QueueResultKey = "queue_result";

    /// <summary>KV key holding the most recent queue results, newest first.</summary>
    public const string QueueHistoryKey = "queue_history";

    /// <summary>KV key the scheduled task always writes its latest result to.</summary>
    public const string ScheduledResultKey = "scheduled_result";

    /// <summary>KV key holding the most recent scheduled runs, newest first.</summary>
    public const string ScheduledHistoryKey = "scheduled_history";

    /// <summary>Cron expression of the scheduled trigger (see [triggers] in wrangler.toml).</summary>
    public const string ScheduledCron = "0 * * * *";

    /// <summary>KV values are capped at this many characters.</summary>
    public const int KvMaxValueLength = 64;

    /// <summary>R2 uploads are capped at this many bytes.</summary>
    public const int R2MaxBytes = 1024;

    /// <summary>Queue messages are capped at this many characters.</summary>
    public const int QueueMaxTextLength = 16;

    /// <summary>How many entries of each history list are kept.</summary>
    public const int HistoryLength = 12;
}
