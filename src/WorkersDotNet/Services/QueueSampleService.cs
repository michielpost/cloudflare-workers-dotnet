using Shared;
using Workers;

namespace WorkersDotNet.Services
{
    /// <summary>
    /// The queue sample's business logic: putting messages on the queue, reading
    /// the results the consumer wrote to KV, and (from the consumer) writing a
    /// processed record plus its history back to KV. The endpoint stays a thin
    /// controller; each method takes the binding it needs.
    /// </summary>
    public static class QueueSampleService
    {
        /// <summary>The result of a produce: the send result, or an error to surface.</summary>
        public sealed record Outcome(QueueSendResult? Result, string? Error, int Status);

        const int MaxTextLength = SampleConfig.QueueMaxTextLength;
        const string QueueName = SampleConfig.QueueName;
        const string NamespaceName = SampleConfig.KvNamespaceName;
        const string ResultKey = SampleConfig.QueueResultKey;
        const string HistoryKey = SampleConfig.QueueHistoryKey;
        const int HistoryLength = SampleConfig.HistoryLength;

        /// <summary>Puts one message on the queue and reports when it was queued.</summary>
        public static async Task<Outcome> ProduceAsync(IQueueProducer queue, string text)
        {
            if (text.Length == 0)
                return new Outcome(null, "The message must not be empty", 400);

            if (text.Length > MaxTextLength)
                return new Outcome(null, $"The message is {text.Length} characters, the limit is {MaxTextLength}.", 400);

            var queuedAt = DateTimeOffset.UtcNow.ToString("O");
            await queue.SendJsonAsync(new QueuedMessage(text, queuedAt, "", 0));

            return new Outcome(
                new QueueSendResult(true, $"Message added to {QueueName}.", text, queuedAt),
                null,
                200);
        }

        /// <summary>Reads what the queue consumer wrote to KV.</summary>
        public static async Task<QueueStatus> ReadStatusAsync(IKvNamespace kv)
        {
            QueueRecord? latest = null;
            try
            {
                latest = await kv.GetJsonAsync<QueueRecord>(ResultKey);
            }
            catch (Exception)
            {
                latest = null;
            }

            // The history is only read when there is a latest result, otherwise
            // the very first call would always log a parse failure.
            var items = new List<QueueRecord>();
            if (latest is not null)
            {
                var history = await kv.GetJsonAsync<QueueHistory>(HistoryKey);
                if (history is not null && history.Items is not null)
                {
                    foreach (var item in history.Items)
                        items.Add(item);
                }
            }

            return new QueueStatus(latest, items, QueueName, NamespaceName, ResultKey, MaxTextLength);
        }

        /// <summary>Writes a processed record and prepends it to the history (used by the consumer).</summary>
        public static async Task WriteQueueResultAsync(IKvNamespace kv, QueueRecord record)
        {
            await kv.PutJsonAsync(ResultKey, record);
            await kv.PutJsonAsync(HistoryKey, await BuildHistoryAsync(kv, record));
        }

        /// <summary>Newest first, capped at <see cref="HistoryLength"/> entries.</summary>
        static async Task<QueueHistory> BuildHistoryAsync(IKvNamespace kv, QueueRecord record)
        {
            var items = new List<QueueRecord>();
            items.Add(record);

            var existing = await kv.GetJsonAsync<QueueHistory>(HistoryKey);
            if (existing is not null && existing.Items is not null)
            {
                foreach (var item in existing.Items)
                {
                    if (items.Count >= HistoryLength)
                        break;

                    items.Add(item);
                }
            }

            return new QueueHistory(items);
        }
    }
}
