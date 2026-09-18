namespace Shared;

// Queue sample: the queue the producer sends to and the consumer reads from, the
// two KV keys the consumer writes and the message length limit.
public static partial class SampleConfig
{
    /// <summary>Queue the producer sample sends to and the consumer sample reads from.</summary>
    public const string QueueName = "dotnet-queue";

    /// <summary>KV key the queue consumer always writes its latest result to.</summary>
    public const string QueueResultKey = "queue_result";

    /// <summary>KV key holding the most recent queue results, newest first.</summary>
    public const string QueueHistoryKey = "queue_history";

    /// <summary>Queue messages are capped at this many characters.</summary>
    public const int QueueMaxTextLength = 16;
}
