namespace Shared;

/// <summary>Newest-first list of past scheduled runs, stored as one KV value.</summary>
public sealed record ScheduledHistory(IReadOnlyList<ScheduledRun> Items);

/// <summary>Result of a single scheduled (cron) run.</summary>
public sealed record ScheduledRun(
    int RunNumber,
    string Cron,
    string ScheduledForUtc,
    string RanAtUtc,
    bool Manual);

/// <summary>Everything GET /api/scheduled returns: the latest run, the history and the cron expression.</summary>
public sealed record ScheduledStatus(ScheduledRun? Latest, IReadOnlyList<ScheduledRun> Items, string Cron, string ResultKey);
