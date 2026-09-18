using Shared;
using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// Sample: a KV namespace (<c>env.Kv("KV")</c>, namespace title
    /// <c>dotnet_test</c>).
    /// </summary>
    /// <remarks>
    /// Values can only be written to three fixed keys and are capped at
    /// <see cref="MaxValueLength"/> characters. GET returns the current value of
    /// every fixed key; POST writes one of them and returns the new snapshot.
    /// Locally the values live in the wrangler key-value store under
    /// <c>.wrangler/state/v3/kv</c>, so nothing is uploaded to Cloudflare while
    /// developing.
    /// </remarks>
    public static class KvEndpoint
    {
        const int MaxValueLength = SampleConfig.KvMaxValueLength;
        const string Binding = "KV";
        const string NamespaceName = SampleConfig.KvNamespaceName;

        const string Key1 = "sample_key_1";
        const string Key2 = "sample_key_2";
        const string Key3 = "sample_key_3";

        public static async Task<Response> HandleAsync(Request request, Env environment)
        {
            var kv = environment.Kv(Binding);

            if (request.Method == "POST")
                return await WriteAsync(request, kv);

            var snapshot = await ReadAsync(kv);
            return Response.Json(snapshot, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>Reads all three fixed keys in one round trip.</summary>
        static async Task<KvSnapshot> ReadAsync(IKvNamespace kv)
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

        static async Task<Response> WriteAsync(Request request, IKvNamespace kv)
        {
            KvWriteRequest? input;
            try
            {
                input = await request.JsonAsync<KvWriteRequest>();
            }
            catch (Exception)
            {
                return Results.Error("Malformed JSON body", 400);
            }

            if (input is null || input.Value is null)
                return Results.Error("A JSON body with \"key\" and \"value\" is required", 400);

            if (!IsFixedKey(input.Key))
                return Results.Error(
                    "Unknown key. Use one of the fixed keys: sample_key_1, sample_key_2, sample_key_3.",
                    400);

            if (input.Value.Length == 0)
                return Results.Error("The value must not be empty", 400);

            if (input.Value.Length > MaxValueLength)
                return Results.Error(
                    $"The value is {input.Value.Length} characters, the limit is {MaxValueLength}.",
                    400);

            await kv.PutTextAsync(input.Key, input.Value);

            // Returning the whole snapshot means the frontend has a single
            // response shape for both the read and the write call.
            var snapshot = await ReadAsync(kv);
            return Response.Json(
                    new KvWriteResult(
                        true,
                        $"Wrote {input.Value.Length} characters to {input.Key}.",
                        snapshot),
                    200)
                .WithHeader("cache-control", "no-store");
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
