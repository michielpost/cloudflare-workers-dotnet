using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;

namespace WorkersDotNet.Services
{
    /// <summary>
    /// The scheduled-task sample's business logic: recording a run and its
    /// newest-first history in KV, and reading it back for the UI. The endpoint
    /// stays a thin controller; the KV binding is injected via <see cref="KvStore"/>.
    /// </summary>
    public sealed class ScheduledSampleService
    {
        readonly string Cron = SampleConfig.ScheduledCron;
        readonly string ResultKey = SampleConfig.ScheduledResultKey;
        readonly string HistoryKey = SampleConfig.ScheduledHistoryKey;
        readonly int HistoryLength = SampleConfig.HistoryLength;

        private readonly KvStore _kv;

        public ScheduledSampleService(KvStore kv)
        {
            _kv = kv;
        }

        /// <summary>
        /// Stores one run under <c>scheduled_result</c> and prepends it to the
        /// newest-first history in <c>scheduled_history</c>. Called by the cron
        /// handler and by the on-demand POST endpoint.
        /// </summary>
        public async Task WriteRunAsync(string cron, string scheduledForUtc, bool manual)
        {
            var previous = await _kv.GetJsonAsync<ScheduledRun>(ResultKey);
            var runNumber = previous is null ? 1 : previous.RunNumber + 1;

            var run = new ScheduledRun(
                runNumber,
                cron,
                scheduledForUtc,
                DateTimeOffset.UtcNow.ToString("O"),
                manual);

            await _kv.PutJsonAsync(ResultKey, run);

            var items = new List<ScheduledRun>();
            items.Add(run);

            var existing = await _kv.GetJsonAsync<ScheduledHistory>(HistoryKey);
            if (existing is not null && existing.Items is not null)
            {
                foreach (var item in existing.Items)
                {
                    if (items.Count >= HistoryLength)
                        break;

                    items.Add(item);
                }
            }

            await _kv.PutJsonAsync(HistoryKey, new ScheduledHistory(items));
        }

        public async Task<ScheduledStatus> ReadStatusAsync()
        {
            var latest = await _kv.GetJsonAsync<ScheduledRun>(ResultKey);

            var items = new List<ScheduledRun>();
            if (latest is not null)
            {
                var history = await _kv.GetJsonAsync<ScheduledHistory>(HistoryKey);
                if (history is not null && history.Items is not null)
                {
                    foreach (var item in history.Items)
                        items.Add(item);
                }
            }

            return new ScheduledStatus(latest, items, Cron, ResultKey);
        }
    }
}
