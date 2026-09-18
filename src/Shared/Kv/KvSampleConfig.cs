namespace Shared;

// KV sample: the namespace the queue consumer, the scheduled task and the KV
// sample itself write to, plus the value length limit.
public static partial class SampleConfig
{
    /// <summary>KV namespace the queue consumer, the scheduled task and the KV sample write to.</summary>
    public const string KvNamespaceName = "dotnet_test";

    /// <summary>KV values are capped at this many characters.</summary>
    public const int KvMaxValueLength = 64;
}
