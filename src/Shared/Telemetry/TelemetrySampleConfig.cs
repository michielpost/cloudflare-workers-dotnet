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
}
