using Shared;
using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// The handlers that are not HTTP requests: the queue consumer and the
    /// scheduled (cron) task.
    /// </summary>
    /// <remarks>
    /// Both write their result to the KV namespace, which is exactly what the
    /// frontend reads back through <see cref="QueueEndpoint"/> and
    /// <see cref="ScheduledEndpoint"/>. The worker has no shared memory between
    /// requests, so the binding is the hand-off.
    /// </remarks>
    public static class WorkerEvents
    {
        const string KvBinding = "KV";
        const string ResultKey = SampleConfig.QueueResultKey;
        const string HistoryKey = SampleConfig.QueueHistoryKey;
        const int HistoryLength = SampleConfig.HistoryLength;

        /// <summary>
        /// Consumes the messages the producer put on <c>dotnet-queue</c> and stores
        /// the text plus the queued/processed dates under the fixed KV key
        /// <c>queue_result</c>.
        /// </summary>
        [Queue]
        public static async Task ConsumeAsync(
            QueueMessageBatch<QueueJob> batch,
            Env environment,
            Context context)
        {
            Console.WriteLine($"Queue consumer received {batch.Count} message(s) from {batch.Queue}");

            foreach (var message in batch)
            {
                var record = new QueueRecord(
                    message.Body.Text,
                    message.Body.QueuedAtUtc,
                    DateTimeOffset.UtcNow.ToString("O"),
                    message.Id);

                await WriteQueueResultAsync(environment, record);
                message.Ack();

                Console.WriteLine($"Queue consumer stored \"{record.Text}\" ({record.MessageId})");
            }
        }

        /// <summary>
        /// Runs every hour (see <c>[triggers]</c> in wrangler.toml) and stores the
        /// run in the KV namespace. The KV write happens after the event returns,
        /// so it is handed to <c>context.WaitUntil</c>.
        /// </summary>
        [Scheduled]
        public static void OnSchedule(ScheduledEvent scheduled, Env environment, Context context)
        {
            Console.WriteLine($"Scheduled task {scheduled.Cron} fired for {scheduled.ScheduledTime:O}");

            context.WaitUntil(
                ScheduledEndpoint.WriteRunAsync(
                    environment,
                    scheduled.Cron,
                    scheduled.ScheduledTime.ToString("O"),
                    false));
        }

        static async Task WriteQueueResultAsync(Env environment, QueueRecord record)
        {
            var kv = environment.Kv(KvBinding);

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
