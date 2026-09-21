using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;

namespace WorkersDotNet.Services
{
    /// <summary>
    /// The KV sample's business logic: reading and writing the three fixed keys.
    /// The endpoint stays a thin controller; this service calls the KV binding
    /// through the injected <see cref="KvStore"/> helper.
    /// </summary>
    /// <summary>The result of a write: the new snapshot, or an error to surface.</summary>
    public sealed record KvOutcome(KvWriteResult? Result, string? Error, int Status);

    public sealed class KvSampleService
    {

        readonly int MaxValueLength = SampleConfig.KvMaxValueLength;
        readonly string NamespaceName = SampleConfig.KvNamespaceName;

        readonly string Key1 = "sample_key_1";
        readonly string Key2 = "sample_key_2";
        readonly string Key3 = "sample_key_3";

        private readonly KvStore _kv;

        public KvSampleService(KvStore kv)
        {
            _kv = kv;
        }

        /// <summary>Reads all three fixed keys in one round trip.</summary>
        public async Task<KvSnapshot> ReadAsync()
        {
            var items = new List<KvItem>();

            var value1 = await _kv.GetTextAsync(Key1);
            items.Add(new KvItem(Key1, value1 ?? "", value1 is not null));

            var value2 = await _kv.GetTextAsync(Key2);
            items.Add(new KvItem(Key2, value2 ?? "", value2 is not null));

            var value3 = await _kv.GetTextAsync(Key3);
            items.Add(new KvItem(Key3, value3 ?? "", value3 is not null));

            return new KvSnapshot(items, MaxValueLength, NamespaceName);
        }

        public async Task<KvOutcome> WriteAsync(KvWriteRequest input)
        {
            if (input is null || input.Value is null)
                return new KvOutcome(null, "A JSON body with \"key\" and \"value\" is required", 400);

            if (!IsFixedKey(input.Key))
                return new KvOutcome(null, "Unknown key. Use one of the fixed keys: sample_key_1, sample_key_2, sample_key_3.", 400);

            if (input.Value.Length == 0)
                return new KvOutcome(null, "The value must not be empty", 400);

            if (input.Value.Length > MaxValueLength)
                return new KvOutcome(null, $"The value is {input.Value.Length} characters, the limit is {MaxValueLength}.", 400);

            await _kv.PutTextAsync(input.Key, input.Value);

            // Returning the whole snapshot means the frontend has a single
            // response shape for both the read and the write call.
            var snapshot = await ReadAsync();
            return new KvOutcome(
                new KvWriteResult(true, $"Wrote {input.Value.Length} characters to {input.Key}.", snapshot),
                null,
                200);
        }

        bool IsFixedKey(string? key)
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
