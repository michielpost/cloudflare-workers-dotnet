namespace Shared;

/// <summary>
/// Body of a message on either of the two queues this worker consumes:
/// <c>dotnet-queue</c> (the queue sample) and <c>dotnet-telemetry</c> (the
/// weather sensor pipeline).
/// </summary>
/// <remarks>
/// Both queues share this shape on purpose: a worker can only have one queue
/// entrypoint (the compiler rejects a second one with WRK111), so the consumer
/// branches on the queue name and therefore needs one type that covers both
/// bodies. Each sample fills the fields it needs and leaves the others empty:
/// <list type="bullet">
/// <item><c>dotnet-queue</c> uses <see cref="Text"/> (the message text) and
/// <see cref="QueuedAtUtc"/> (when it was put on the queue).</item>
/// <item><c>dotnet-telemetry</c> uses <see cref="Text"/> (the sensor id),
/// <see cref="JobId"/> (the row id of the job) and <see cref="QueuedAtMs"/>.</item>
/// </list>
/// <see cref="QueuedAtUtc"/> is an ISO 8601 string because the queue sample only
/// shows it, while <see cref="QueuedAtMs"/> is unix milliseconds because the
/// telemetry sample stores and compares it in D1.
/// </remarks>
public sealed record QueuedMessage(
    string Text,
    string QueuedAtUtc,
    string JobId,
    long QueuedAtMs);
