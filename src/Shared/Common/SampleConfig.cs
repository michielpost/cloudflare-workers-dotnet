namespace Shared;

/// <summary>
/// Names and limits shared by the worker and the Blazor frontend. The worker
/// also returns the names and limits in its API payloads, so the frontend never
/// has to guess them.
/// </summary>
/// <remarks>
/// Declared <c>partial</c> so every sample keeps its own constants next to the
/// models it belongs to: see <c>D1/D1SampleConfig.cs</c>,
/// <c>Kv/KvSampleConfig.cs</c> and the other <c>*SampleConfig.cs</c> files in
/// this folder tree. The value below is shared by more than one sample.
/// </remarks>
public static partial class SampleConfig
{
    /// <summary>How many entries of each history list are kept (queue and scheduled task).</summary>
    public const int HistoryLength = 12;
}
