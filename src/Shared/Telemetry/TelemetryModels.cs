namespace Shared;

/// <summary>
/// Everything the weather sensor pipeline uses to talk to the frontend. All
/// timestamps here are unix milliseconds, exactly as the rows are stored in D1,
/// and 0 means "not set yet" (the frontend formats them for display).
/// </summary>
/// <remarks>
/// The pipeline is the end-to-end sample: a cron trigger picks the sensors that
/// are due, puts one job per sensor on a queue, the consumer takes a lease at the
/// RateGate Durable Object and then calls the weather API, and the reading lands
/// in D1.
/// </remarks>
public sealed record SensorInfo(
    string SensorId,
    string Name,
    double Latitude,
    double Longitude,
    int IntervalMinutes,
    long NextReadAtMs,
    int Active,
    int Reads,
    int Pending);

/// <summary>One pipeline job and how far it got (queued, done or failed).</summary>
public sealed record JobInfo(
    string Id,
    string SensorId,
    string Status,
    int Attempts,
    string LastError,
    long QueuedAtMs,
    long ProcessedAtMs);

/// <summary>
/// One finished reading. Source is "live" for a real API call and "simulated"
/// for the fallback used when the provider cannot be reached.
/// </summary>
public sealed record ReadingInfo(
    string Id,
    string SensorId,
    double Temperature,
    double AirQuality,
    string Condition,
    string Source,
    long ReadingAtMs,
    long ProcessedAtMs);

/// <summary>State of the RateGate Durable Object that serialises outbound calls.</summary>
public sealed record GateInfo(
    bool Busy,
    string Owner,
    int LeaseSeconds,
    long ExpiresAtMs);

/// <summary>Counters shown at the top of the telemetry page.</summary>
public sealed record PipelineStats(int Sensors, int Readings, int Queued, int Done, int Failed, int Attempts);

/// <summary>Everything GET /api/telemetry returns.</summary>
public sealed record TelemetryStatus(
    IReadOnlyList<SensorInfo> Sensors,
    IReadOnlyList<ReadingInfo> Readings,
    IReadOnlyList<JobInfo> Jobs,
    PipelineStats Stats,
    GateInfo Gate,
    string DatabaseName,
    string QueueName,
    string Cron,
    int RateLimitSeconds,
    int MaxAttempts,
    string WeatherApi);

/// <summary>Result of running the pipeline on demand (POST /api/telemetry/run).</summary>
public sealed record TelemetryRunResult(
    bool Ok,
    string Message,
    int Enqueued,
    IReadOnlyList<string> SensorIds,
    TelemetryStatus Status);

/// <summary>
/// Answer of the RateGate Durable Object: whether the caller may make an
/// outbound call now, which job holds the lease (<see cref="Owner"/>) and until
/// when it lasts. <see cref="Token"/> is what the holder passes back to
/// <c>complete</c>, so a slow job cannot release a newer job's lease.
/// </summary>
public sealed record Lease(bool Allowed, string Token, string Owner, long ExpiresAtMs);
