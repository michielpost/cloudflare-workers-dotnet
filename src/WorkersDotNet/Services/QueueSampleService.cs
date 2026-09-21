using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;
using Workers;

namespace WorkersDotNet.Services
{
    /// <summary>
    /// The queue sample's business logic: putting messages on the queue, reading
    /// the results the consumer wrote to KV, and (from the consumer) writing a
    /// processed record plus its history back to KV. The endpoint stays a thin
    /// controller; the queue and KV bindings are injected.
    /// </summary>
    /// <summary>The result of a produce: the send result, or an error to surface.</summary>
    public sealed record QueueOutcome(QueueSendResult? Result, string? Error, int Status);

    public sealed class QueueSampleService
    {

        readonly int MaxTextLength = SampleConfig.QueueMaxTextLength;
        readonly string QueueName = SampleConfig.QueueName;
        readonly string NamespaceName = SampleConfig.KvNamespaceName;
        readonly string ResultKey = SampleConfig.QueueResultKey;
        readonly string HistoryKey = SampleConfig.QueueHistoryKey;
        readonly int HistoryLength = SampleConfig.HistoryLength;

        private readonly IQueueProducer _queue;
        private readonly KvStore _kv;

        public QueueSampleService(IQueueProducer queue, KvStore kv)
        {
            _queue = queue;
            _kv = kv;
        }

        /// <summary>Puts one message on the queue and reports when it was queued.</summary>
        public async Task<QueueOutcome> ProduceAsync(string text)
        {
            if (text.Length == 0)
                return new QueueOutcome(null, "The message must not be empty", 400);

            if (text.Length > MaxTextLength)
                return new QueueOutcome(null, $"The message is {text.Length} characters, the limit is {MaxTextLength}.", 400);

            var queuedAt = DateTimeOffset.UtcNow.ToString("O");
            await _queue.SendJsonAsync(new QueuedMessage(text, queuedAt, "", 0));

            return new QueueOutcome(
                new QueueSendResult(true, $"Message added to {QueueName}.", text, queuedAt),
                null,
                200);
        }

        /// <summary>Reads what the queue consumer wrote to KV.</summary>
        public async Task<QueueStatus> ReadStatusAsync()
        {
            var latest = await _kv.GetJsonAsync<QueueRecord>(ResultKey);

            var items = new List<QueueRecord>();
            if (latest is not null)
            {
                var history = await _kv.GetJsonAsync<QueueHistory>(HistoryKey);
                if (history is not null && history.Items is not null)
                {
                    foreach (var item in history.Items)
                        items.Add(item);
                }
            }

            return new QueueStatus(latest, items, QueueName, NamespaceName, ResultKey, MaxTextLength);
        }

        /// <summary>Writes a processed record and prepends it to the history (used by the consumer).</summary>
        public async Task WriteQueueResultAsync(QueueRecord record)
        {
            await _kv.PutJsonAsync(ResultKey, record);
            await _kv.PutJsonAsync(HistoryKey, await BuildHistoryAsync(record));
        }

        /// <summary>Newest first, capped at <see cref="HistoryLength"/> entries.</summary>
        async Task<QueueHistory> BuildHistoryAsync(QueueRecord record)
        {
            var items = new List<QueueRecord>();
            items.Add(record);

            var existing = await _kv.GetJsonAsync<QueueHistory>(HistoryKey);
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
