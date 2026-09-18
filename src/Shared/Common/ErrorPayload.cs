namespace Shared;

/// <summary>
/// Body the worker returns for a rejected request (see <c>Results.Error</c> in
/// the worker), so the frontend can show the reason instead of raw text.
/// </summary>
public sealed record ErrorPayload(string Error, string RequestId);
