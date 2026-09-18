namespace Shared;

/// <summary>
/// Body of the validation sample (POST /api/policy): a device reading the worker
/// accepts or rejects. Used by the Basics page.
/// </summary>
public sealed record ReadingInput(string DeviceId, double Value, IReadOnlyList<string> Tags);
