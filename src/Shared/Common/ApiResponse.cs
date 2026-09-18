namespace Shared;

/// <summary>
/// The answer the worker's simple samples return: whether the call worked, a
/// human readable message, the path that was called and when the worker handled
/// it. <c>Results.Ok</c> in the worker builds these.
/// </summary>
public sealed record ApiResponse(bool Ok, string Message, string Path, DateTimeOffset Timestamp);
