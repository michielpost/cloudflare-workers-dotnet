using System.Globalization;

namespace BlazorWebApp.Services;

/// <summary>Formatting helpers shared by the sample pages.</summary>
public static class Fmt
{
    /// <summary>Renders an ISO-8601 timestamp from the worker as local time.</summary>
    public static string Local(string? isoTimestamp, string format = "HH:mm:ss")
        => DateTimeOffset.TryParse(isoTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value.ToLocalTime().ToString(format, CultureInfo.InvariantCulture)
            : "—";

    /// <summary>How long ago a timestamp was, e.g. "2.4 s ago".</summary>
    public static string Ago(string? isoTimestamp)
    {
        if (!DateTimeOffset.TryParse(isoTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            return "—";

        return Ago(value);
    }

    /// <summary>How long ago a timestamp was, e.g. "2.4 s ago".</summary>
    public static string Ago(DateTimeOffset value)
    {
        var delta = DateTimeOffset.UtcNow - value.ToUniversalTime();

        if (delta.TotalSeconds < 0)
            delta = TimeSpan.Zero;

        return delta.TotalSeconds < 60
            ? $"{delta.TotalSeconds:0.0} s ago"
            : delta.TotalMinutes < 60
                ? $"{delta.TotalMinutes:0.0} min ago"
                : $"{delta.TotalHours:0.0} h ago";
    }

    /// <summary>Time between two worker timestamps, e.g. "1.02 s".</summary>
    public static string Between(string? fromIso, string? toIso)
    {
        if (!DateTimeOffset.TryParse(fromIso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            || !DateTimeOffset.TryParse(toIso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
            return "—";

        var delta = to - from;
        return delta.TotalSeconds < 60
            ? $"{delta.TotalSeconds:0.00} s"
            : $"{delta.TotalMinutes:0.00} min";
    }

    public static string Bytes(double bytes) => bytes switch
    {
        < 1024 => $"{bytes:0} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / 1048576.0:0.0} MB"
    };

    /// <summary>
    /// Renders one of the worker's unix-millisecond timestamps as local time.
    /// The D1 rows store integers, so <see cref="Local"/> cannot be used for them.
    /// </summary>
    public static string Clock(long unixMilliseconds, string format = "HH:mm:ss")
        => unixMilliseconds <= 0
            ? "—"
            : DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds)
                .ToLocalTime()
                .ToString(format, CultureInfo.InvariantCulture);

    /// <summary>How long ago one of the worker's unix-millisecond timestamps was.</summary>
    public static string AgoMs(long unixMilliseconds)
        => unixMilliseconds <= 0
            ? "—"
            : Ago(DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds));

    /// <summary>Time between two unix-millisecond timestamps, e.g. "412 ms".</summary>
    public static string BetweenMs(long fromMilliseconds, long toMilliseconds)
    {
        if (fromMilliseconds <= 0 || toMilliseconds <= 0)
            return "—";

        var elapsed = Math.Max(0, toMilliseconds - fromMilliseconds);

        return elapsed < 1000
            ? $"{elapsed} ms"
            : $"{elapsed / 1000.0:0.00} s";
    }
}
