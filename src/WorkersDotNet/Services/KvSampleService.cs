using Shared;
using Workers;

namespace WorkersDotNet.Services
{
    /// <summary>
    /// The KV sample's business logic: reading and writing the three fixed keys.
    /// The endpoint stays a thin controller; this service calls the KV binding
    /// directly.
    /// </summary>
    public static class KvSampleService
    {
        /// <summary>The result of a write: the new snapshot, or an error to surface.</summary>
        public sealed record Outcome(KvWriteResult? Result, string? Error, int Status);

        const int MaxValueLength = SampleConfig.KvMaxValueLength;
        const string NamespaceName = SampleConfig.KvNamespaceName;

        const string Key1 = "sample_key_1";
        const string Key2 = "sample_key_2";
        const string Key3 = "sample_key_3";

        /// <summary>Reads all three fixed keys in one round trip.</summary>
        public static async Task<KvSnapshot> ReadAsync(IKvNamespace kv)
        {
            var items = new List<KvItem>();

            var value1 = await kv.GetTextAsync(Key1);
            items.Add(new KvItem(Key1, value1 ?? "", value1 is not null));

            var value2 = await kv.GetTextAsync(Key2);
            items.Add(new KvItem(Key2, value2 ?? "", value2 is not null));

            var value3 = await kv.GetTextAsync(Key3);
            items.Add(new KvItem(Key3, value3 ?? "", value3 is not null));

            return new KvSnapshot(items, MaxValueLength, NamespaceName);
        }

        public static async Task<Outcome> WriteAsync(IKvNamespace kv, KvWriteRequest input)
        {
            if (input is null || input.Value is null)
                return new Outcome(null, "A JSON body with \"key\" and \"value\" is required", 400);

            if (!IsFixedKey(input.Key))
                return new Outcome(null, "Unknown key. Use one of the fixed keys: sample_key_1, sample_key_2, sample_key_3.", 400);

            if (input.Value.Length == 0)
                return new Outcome(null, "The value must not be empty", 400);

            if (input.Value.Length > MaxValueLength)
                return new Outcome(null, $"The value is {input.Value.Length} characters, the limit is {MaxValueLength}.", 400);

            await kv.PutTextAsync(input.Key, input.Value);

            // Returning the whole snapshot means the frontend has a single
            // response shape for both the read and the write call.
            var snapshot = await ReadAsync(kv);
            return new Outcome(
                new KvWriteResult(true, $"Wrote {input.Value.Length} characters to {input.Key}.", snapshot),
                null,
                200);
        }

        static bool IsFixedKey(string? key)
        {
            if (key is null)
                return false;
            if (key == Key1)
                return true;
            if (key == Key2)
                return true;
            return key == Key3;
        }
    }
}
