namespace Shared;

/// <summary>Request body of the queue producer sample.</summary>
public sealed record QueueMessageInput(string Text);

/// <summary>Answer of the producer: what was queued and when.</summary>
public sealed record QueueSendResult(bool Ok, string Message, string Text, string QueuedAtUtc);

/// <summary>
/// One processed queue message: the text, when it was put on the queue and when
/// the consumer processed it.
/// </summary>
public sealed record QueueRecord(string Text, string QueuedAtUtc, string ProcessedAtUtc, string MessageId);

/// <summary>Everything GET /api/queue returns: the latest result, the history and the names.</summary>
public sealed record QueueStatus(
    QueueRecord? Latest,
    IReadOnlyList<QueueRecord> Items,
    string QueueName,
    string NamespaceName,
    string ResultKey,
    int MaxTextLength);

/// <summary>Newest-first list of processed queue messages, stored as one KV value.</summary>
public sealed record QueueHistory(IReadOnlyList<QueueRecord> Items);
