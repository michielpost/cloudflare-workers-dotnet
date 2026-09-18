namespace Shared;

public sealed record ApiResponse(bool Ok, string Message, string Path, DateTimeOffset Timestamp);

public sealed record ReadingInput(string DeviceId, double Value, IReadOnlyList<string> Tags);
