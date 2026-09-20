using Shared;
using Workers;

namespace WorkersDotNet.Services
{
    /// <summary>
    /// The scheduled-task sample's business logic: recording a run and its
    /// newest-first history in KV, and reading it back for the UI. The endpoint
    /// stays a thin controller; each method takes the KV binding it needs.
    /// </summary>
    public static class ScheduledSampleService
    {
        const string Cron = SampleConfig.ScheduledCron;
        const string ResultKey = SampleConfig.ScheduledResultKey;
        const string HistoryKey = SampleConfig.ScheduledHistoryKey;
        const int HistoryLength = SampleConfig.HistoryLength;

        /// <summary>
        /// Stores one run under <c>scheduled_result</c> and prepends it to the
        /// newest-first history in <c>scheduled_history</c>. Called by the cron
        /// handler and by the on-demand POST endpoint.
        /// </summary>
        public static async Task WriteRunAsync(IKvNamespace kv, string cron, string scheduledForUtc, bool manual)
        {
            var previous = await kv.GetJsonAsync<ScheduledRun>(ResultKey);
            var runNumber = previous is null ? 1 : previous.RunNumber + 1;

            var run = new ScheduledRun(
                runNumber,
                cron,
                scheduledForUtc,
                DateTimeOffset.UtcNow.ToString("O"),
                manual);

            await kv.PutJsonAsync(ResultKey, run);

            var items = new List<ScheduledRun>();
            items.Add(run);

            var existing = await kv.GetJsonAsync<ScheduledHistory>(HistoryKey);
            if (existing is not null && existing.Items is not null)
            {
                foreach (var item in existing.Items)
                {
                    if (items.Count >= HistoryLength)
                        break;

                    items.Add(item);
                }
            }

            await kv.PutJsonAsync(HistoryKey, new ScheduledHistory(items));
        }

        public static async Task<ScheduledStatus> ReadStatusAsync(IKvNamespace kv)
        {
            ScheduledRun? latest = null;
            try
            {
                latest = await kv.GetJsonAsync<ScheduledRun>(ResultKey);
            }
            catch (Exception)
            {
                latest = null;
            }

            var items = new List<ScheduledRun>();
            if (latest is not null)
            {
                var history = await kv.GetJsonAsync<ScheduledHistory>(HistoryKey);
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
