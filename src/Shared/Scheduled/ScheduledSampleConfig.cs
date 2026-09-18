namespace Shared;

// Scheduled task sample: the two KV keys the hourly run writes and its cron
// expression (which is also the one in [triggers] in wrangler.toml).
public static partial class SampleConfig
{
    /// <summary>KV key the scheduled task always writes its latest result to.</summary>
    public const string ScheduledResultKey = "scheduled_result";

    /// <summary>KV key holding the most recent scheduled runs, newest first.</summary>
    public const string ScheduledHistoryKey = "scheduled_history";

    /// <summary>Cron expression of the scheduled trigger (see [triggers] in wrangler.toml).</summary>
    public const string ScheduledCron = "0 * * * *";
}
