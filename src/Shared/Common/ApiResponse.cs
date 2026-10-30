namespace Shared;

/// <summary>
/// The answer the worker's simple samples return: whether the call worked, a
/// human readable message, the path that was called and when the worker handled
/// it. <c>Results.Ok</c> in the worker builds these.
/// </summary>
/// <remarks>
/// <see cref="Timestamp"/> is an ISO 8601 string rather than a
/// <c>DateTimeOffset</c>: the Workers C# profile has no <c>DateTimeOffset</c>, so
/// the worker reads the clock through the runtime's <c>Date</c> type.
/// </remarks>
public sealed record ApiResponse(bool Ok, string Message, string Path, string Timestamp);
