namespace Shared;

// Weather sensor pipeline: the queue the cron trigger produces to, the cron
// expression itself, the limits the consumer enforces and the weather API it calls.
public static partial class SampleConfig
{
    /// <summary>
    /// Queue the telemetry cron trigger sends sensor jobs to, and the consumer
    /// reads from (see [[queues.*]] in wrangler.toml).
    /// </summary>
    public const string TelemetryQueueName = "dotnet-telemetry";

    /// <summary>Cron expression of the telemetry trigger: every 5 minutes.</summary>
    public const string TelemetryCron = "*/5 * * * *";

    /// <summary>How many sensors one cron run queues at most.</summary>
    public const int TelemetryBatchSize = 10;

    /// <summary>How many readings the telemetry page shows at most.</summary>
    public const int TelemetryReadingLimit = 20;

    /// <summary>Seconds one outbound weather call is allowed to take.</summary>
    public const int TelemetryFetchTimeoutSeconds = 5;

    /// <summary>Seconds the RateGate lease is held, so outbound calls are serialised.</summary>
    public const int TelemetryRateLimitSeconds = 30;

    /// <summary>Attempts before a job is marked failed instead of retried.</summary>
    public const int TelemetryMaxAttempts = 4;

    /// <summary>The real weather API the pipeline calls (no key needed).</summary>
    public const string WeatherApiName = "open-meteo.com";

    /// <summary>
    /// How many days of telemetry are kept. The cron trigger deletes every
    /// reading and job from before the current UTC day, so the tables only ever
    /// hold today's rows.
    /// </summary>
    /// <remarks>
    /// The status read counts rows, so this window is also the ceiling on what
    /// one read can cost: 3 sensors x 12 readings/hour x 24 hours is about 860
    /// rows per table. Keep it in step with
    /// <see cref="TelemetryStatusCacheSeconds"/> - a longer window needs a longer
    /// cache to stay inside the D1 free tier.
    /// </remarks>
    public const int TelemetryRetentionDays = 1;

    /// <summary>
    /// Minutes after which a job that is still queued or retrying is treated as
    /// dead and deleted. A job that has not finished in this long has lost its
    /// queue message, so it would otherwise sit in the table forever and keep
    /// showing up as pending work.
    /// </summary>
    public const int TelemetryStaleJobMinutes = 60;

    /// <summary>
    /// Seconds a GET /api/telemetry answer is served from the Cache API. The
    /// telemetry page polls, so without this every open tab would read D1 every
    /// few seconds.
    /// </summary>
    public const int TelemetryStatusCacheSeconds = 20;
}
